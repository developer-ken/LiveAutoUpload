using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LiveAutoUpload.Models
{
    public class FileEventArgs : EventArgs
    {
        public string FilePath, SessionId;
    }
}
