﻿using BiliApi;
using BiliApi.VideoSubmitting;
using FlvUtility;
using LiveAutoUpload.Modles;
using QRCoder;
using TouchSocket.Core;
using TouchSocket.Http;
using TouchSocket.Sockets;

namespace LiveAutoUpload
{
    internal class Program
    {
        const int Port = 8080;
        static string FilePathPrefix = "/ramdisk/biliverec";
        static string cookies = "";

        static Dictionary<string, RecordingSession> session = new Dictionary<string, RecordingSession>();
        static Queue<RecordingSession> _sessions = new Queue<RecordingSession>();
        static BiliSession bsession;

        static void Main(string[] args)
        {
            if (args.Length == 1 && Directory.Exists(args[0]))
            {
                FilePathPrefix = args[0];
                Console.WriteLine($"指定的录播姬工作路径：{FilePathPrefix}");
            }
            else if (Directory.Exists(FilePathPrefix))
            {
                Console.WriteLine($"您没有指定录播姬工作路径！将使用开发者默认值：{FilePathPrefix}");
            }
            else
            {
                Console.WriteLine($"您没有指定录播姬工作路径，且开发者默认值不可用。\n" +
                    $"您必须提供录播姬使用的工作目录，以便本程序读取录播文件。");
            }
            if (File.Exists("bili_login.key"))
            {
                cookies = File.ReadAllText("bili_login.key");
            }
            var qr = new BiliApi.Auth.QRLogin(cookies);
            if (!qr.LoggedIn)
            {
                qr = new BiliApi.Auth.QRLogin();
                {
                    QRCodeGenerator qrGenerator = new QRCodeGenerator();
                    QRCodeData qrCodeData = qrGenerator.CreateQrCode(new PayloadGenerator.Url(qr.QRToken.ScanUrl), QRCodeGenerator.ECCLevel.M);
                    AsciiQRCode qrCode = new AsciiQRCode(qrCodeData);
                    Console.WriteLine(qrCode.GetGraphic(1));
                }
                Console.WriteLine("扫描上面的二维码，登录您的B站账号。");
                qr.Login();
                Console.WriteLine("登录成功");
            }
            else
            {
                Console.WriteLine("登录成功");
            }

            bsession = new BiliSession(qr.Cookies);

            File.WriteAllText("bili_login.key", bsession.GetCookieString());

            HttpService service = new HttpService();
            service.Setup(new TouchSocketConfig()
            .SetListenIPHosts(Port)
            .ConfigureContainer(a =>
            {
                a.AddConsoleLogger();
            })
            .ConfigurePlugins(a =>
            {
                a.Add<EventFire>();
                a.UseDefaultHttpServicePlugin();
            }));
            service.StartAsync();
            EventFire.Instance.RecordingStarted += Instance_RecordingStarted;
            EventFire.Instance.RecordingEnded += Instance_RecordingEnded;
            EventFire.Instance.FileStarted += Instance_FileStarted;
            EventFire.Instance.FileEnded += Instance_FileEnded;

            Console.WriteLine("请在录播姬添加Webhook v2:");
            Console.WriteLine("http://127.0.0.1:8080/api/bilive/v2");
            Console.WriteLine("本软件将在直播结束时自动将视频上传到您登录的B站账号；");
            Console.WriteLine("本地视频将在上传完成后被删除！");

            while (true)
            {
                Thread.Sleep(0);
                if (_sessions.Count > 0)
                {
                    var sess = _sessions.Dequeue();
                    Task.Run(() =>
                    {
                        sess.liveroom.sendDanmaku("本期回放将自动上传到本账号");
                        Console.WriteLine("自动投稿开始");
                        Console.WriteLine("等待所有文件写入完成...");
                        sess.WaitForFileClose();
                        if (sess.Files.Count == 0)
                        {
                            Console.WriteLine("不投稿：没有任何录制文件");
                            return;
                        }
                        string filename = Path.Combine(FilePathPrefix, sess.Files.Keys.First());
                        if (sess.Files.Count > 1)
                        {
                            Console.WriteLine("多个文件，正在合并...");
                            var directory = Path.GetDirectoryName(filename);
                            filename = Path.Combine(directory, Guid.NewGuid().ToString().Replace("-", "") + ".flv");
                            FlvMerger merger = new FlvMerger(filename);
                            foreach (var file in sess.Files.Keys)
                            {
                                Console.WriteLine($" - 处理：{file}");
                                merger.AppendAsync(Path.Combine(FilePathPrefix, file)).Wait();
                                Console.WriteLine($" - 删除：{file}");
                                File.Delete(Path.Combine(FilePathPrefix, file));
                            }
                            Console.WriteLine(" - 等待写缓冲...");
                            merger.Close().Wait();
                            Console.WriteLine(" - 合并完成");
                        }

                        VideoSubmitSession vss = new VideoSubmitSession(bsession.GetCookieString(), filename);
                        Console.WriteLine("申请上传...");
                        vss.PreUpload().Wait();
                        Console.WriteLine("正在传输...");
                        vss.Upload().Wait();
                        Console.WriteLine("传输完成，正在投稿...");
                        var form = new BiliVideoForm
                        {
                            Title = $"[{sess.AreaNameChild}]{sess.Title}",
                            Description = $"[录播回放]\n" +
                            $"直播间号：{sess.RoomId}\n" +
                            $"直播主播：@{sess.Name}\n" +
                            $"直播标题：{sess.Title}\n" +
                            $"直播时段：{sess.StartTime.ToString("yyyy-MM-dd HH:mm:ss")} ~ {sess.EndTime.ToString("yyyy-MM-dd HH:mm:ss")}\n" +
                            $"投稿时间：{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}\n" +
                            $"本视频为上述时段直播的完整回放，仅作为粉丝回顾使用，禁止商用获利\n" +
                            $"LiveAutoUpload - 录播投稿自动化系统 by @亿只鸡蛋_Egg",
                        };
                        Console.WriteLine(" - 计算Tag...");
                        form.Tags = $"录播,{sess.Name}," + vss.GetRecommendedTags(form.Title, 3, desc: form.Description).Result;

                        Console.WriteLine(" - 提交视频信息...");

                        var bvid = vss.Submit(form).Result;
                        Console.WriteLine($"投稿完成({bvid})");
                        Console.WriteLine($" - 删除 {filename}");
                        File.Delete(filename);
                        Console.WriteLine($" - 释放会话 {sess.GUID}");
                        session.Remove(sess.GUID);
                        sess.liveroom.sendDanmaku($"直播回放：{bvid}");
                        sess.liveroom.sendDanmaku("等待审核通过后方可观看");
                    });
                }
            }
        }

        private static void Instance_FileEnded(object? sender, Models.FileEventArgs e)
        {
            if (session.ContainsKey(e.SessionId))
            {
                session[e.SessionId].RegFileFinished(e.FilePath);
                Console.WriteLine($"已完成的录播片段:{e.FilePath}");
            }
            else
            {
                Console.WriteLine($"！未被登记的录播片段:{e.FilePath}");
            }
        }

        private static void Instance_FileStarted(object? sender, Models.FileEventArgs e)
        {
            if (!session.ContainsKey(e.SessionId))
            {
                session.Add(e.SessionId, new RecordingSession(e.SessionId, "", DateTime.Now));
            }
            session[e.SessionId].AddFile(e.FilePath);
            Console.WriteLine($"新的录播片段文件:{e.FilePath}");
        }

        private static async void Instance_RecordingEnded(object? sender, Modles.RecordingEventArgs e)
        {
            if (session.ContainsKey(e.SessionId))
            {
                Console.WriteLine($"等待5s断线恢复窗口...");
                await Task.Delay(5000);
                if (!session.ContainsKey(e.SessionId))
                {
                    Console.WriteLine($" - 断线恢复成功，继续等待录制。");
                }
                else
                {
                    Console.WriteLine($" - 没有发生重连，判定直播结束。");
                    session[e.SessionId].EndSession(DateTime.Now);
                    Console.WriteLine($"录播结束《{e.Title}》@{e.AreaNameChild}");
                    Console.WriteLine($" - 转移至投稿队列...");
                    _sessions.Enqueue(session[e.SessionId]);
                    Console.WriteLine($" - OK");
                }
            }
            else
            {
                Console.WriteLine($"！未被登记的录播结束《{e.Title}》@{e.AreaNameChild}");
            }
        }

        private static void Instance_RecordingStarted(object? sender, Modles.RecordingEventArgs e)
        {
            var siroom = GetSessionWithSameRoom(e.RoomId);
            if (siroom is not null && (
                siroom.Title == e.Title &&
                siroom.AreaNameChild == e.AreaNameChild &&
                !siroom.isEnded
                ))
            {//信息相同，大概率是断线了
                Console.WriteLine($"检测到潜在的断线重连，移动数据：{siroom.GUID} -> {e.SessionId}");
                session.Remove(siroom.GUID); //移除旧的信息
                siroom.GUID = e.SessionId; //更新GUID
                session.Add(siroom.GUID, siroom);
                session[e.SessionId].liveroom.sendDanmaku("直播流断线恢复成功");
            }
            else
            {
                if (!session.ContainsKey(e.SessionId))
                {

                    session.Add(e.SessionId, new RecordingSession(e.SessionId, e.Title, DateTime.Now)
                    {
                        AreaNameChild = e.AreaNameChild,
                        Name = e.Name,
                        RoomId = e.RoomId,
                        liveroom = new BiliLiveRoom((int)e.RoomId, bsession)
                    });
                    session[e.SessionId].liveroom.sendDanmaku("直播流已连接");

                }
                else
                {
                    session[e.SessionId].Title = e.Title;
                    session[e.SessionId].AreaNameChild = e.AreaNameChild;
                }
                Console.WriteLine($"开始录制《{e.Title}》@{e.AreaNameChild}");
            }
        }

        private static RecordingSession? GetSessionWithSameRoom(long roomid)
        {
            foreach (var ss in session)
            {
                if (ss.Value.RoomId == roomid)
                {
                    return ss.Value;
                }
            }
            return null;
        }
    }
}