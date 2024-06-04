using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WorkerCSharpWrapper
{
    public class TraceOnlineMessage(string message, int port)
    {
        public  string message = message;
        public  int port = port;
    }
}
