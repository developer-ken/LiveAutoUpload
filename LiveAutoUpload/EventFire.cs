using LiveAutoUpload.Models;
using LiveAutoUpload.Modles;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TouchSocket.Core;
using TouchSocket.Http;

namespace LiveAutoUpload
{
    public class EventFire : PluginBase, IHttpPlugin<IHttpSocketClient>
    {
        public static EventFire Instance;

        public event EventHandler<FileEventArgs> FileStarted;
        public event EventHandler<FileEventArgs> FileEnded;
        public event EventHandler<RecordingEventArgs> RecordingStarted;
        public event EventHandler<RecordingEventArgs> RecordingEnded;

        public EventFire()
        {
            Instance = this;
        }

        public async Task OnHttpRequest(IHttpSocketClient client, HttpContextEventArgs e)
        {
            if (e.Context.Request.Method != TouchSocket.Http.HttpMethod.Post)
            {
                e.Context.Response.StatusCode = 405;
                await e.Context.Response.AnswerAsync();
                return;
            }
            if (e.Context.Request.RelativeURL.StartsWith("/api/bilive/v2"))
            {
                if(e.Context.Request.TryGetContent(out var content))
                {
                    JObject jb = JObject.Parse(Encoding.UTF8.GetString(content));
                    e.Context.Response.StatusCode = 204;
                    await e.Context.Response.AnswerAsync();
                    {
                        switch (jb["EventType"]?.ToString() ?? "")
                        {
                            case "FileOpening":
                                FileStarted?.Invoke(this, new FileEventArgs()
                                {
                                    SessionId = jb["EventData"]?["SessionId"]?.ToString() ?? "",
                                    FilePath = jb["EventData"]?["RelativePath"]?.ToString() ?? ""
                                });
                                break;
                            case "FileClosed":
                                FileEnded?.Invoke(this, new FileEventArgs()
                                {
                                    SessionId = jb["EventData"]?["SessionId"]?.ToString() ?? "",
                                    FilePath = jb["EventData"]?["RelativePath"]?.ToString() ?? ""
                                });
                                break;
                            case "SessionStarted":
                                RecordingStarted?.Invoke(this, new RecordingEventArgs()
                                {
                                    SessionId = jb["EventData"]?["SessionId"]?.ToString() ?? "",
                                    Title = jb["EventData"]?["Title"]?.ToString() ?? "",
                                    AreaNameChild = jb["EventData"]?["AreaNameChild"]?.ToString() ?? "",
                                    RoomId = long.Parse(jb["EventData"]?["RoomId"]?.ToString() ?? "0"),
                                    Name = jb["EventData"]?["Name"]?.ToString() ?? ""
                                });
                                break;
                            case "SessionEnded":
                                RecordingEnded?.Invoke(this, new RecordingEventArgs()
                                {
                                    SessionId = jb["EventData"]?["SessionId"]?.ToString() ?? "",
                                    Title = jb["EventData"]?["Title"]?.ToString() ?? "",
                                    AreaNameChild = jb["EventData"]?["AreaNameChild"]?.ToString() ?? "",
                                    RoomId = long.Parse(jb["EventData"]?["RoomId"]?.ToString() ?? "0"),
                                    Name = jb["EventData"]?["Name"]?.ToString() ?? ""
                                });
                                break;
                        }
                    }
                    return;
                }
                else
                {
                    e.Context.Response.StatusCode = 500;
                    await e.Context.Response.AnswerAsync();
                    return;
                }
            }
            e.Context.Response.StatusCode = 404;
            await e.Context.Response.AnswerAsync();
        }
    }
}
