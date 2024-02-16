using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LiveAutoUpload.Modles
{
    public class RecordingEventArgs : EventArgs
    {
        public string SessionId, Title, AreaNameChild, Name;
        public long RoomId;
    }
}
