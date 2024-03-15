using BiliApi;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LiveAutoUpload.Modles
{
    public class RecordingSession
    {
        public string GUID = "";
        public string Title = "";
        public string AreaNameChild = "";
        public string Name = "";
        public long RoomId = 0;
        public DateTime StartTime;
        public DateTime EndTime;
        public Dictionary<string, bool> Files = new Dictionary<string, bool>();
        public BiliLiveRoom liveroom;
        public bool isEnded = false;

        public RecordingSession(string guid, string title, DateTime startTime)
        {
            GUID = guid;
            Title = title;
            StartTime = startTime;
        }

        public void AddFile(string path)
        {
            Files[path] = true;
        }

        public bool RegFileFinished(string path)
        {
            if (Files.ContainsKey(path))
            {
                Files[path] = false;
                return true;
            }
            else
                return false;
        }

        public void EndSession(DateTime endTime)
        {
            EndTime = endTime;
            isEnded = true;
        }

        public void WaitForFileClose()
        {
            while (true)
            {
                bool notfinished = false;
                foreach(var file in Files)
                {
                    if (file.Value)
                    {
                        notfinished = true;
                    }
                }
                if(!notfinished)
                {
                    break;
                }
                Thread.Sleep(0);
            }
        }
    }
}
