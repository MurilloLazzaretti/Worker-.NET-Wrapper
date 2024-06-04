using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WorkerCSharpWrapper;
using ZapMQ;

namespace SimpleSample
{
    public class Main
    {
        private WorkerWrapperCore worker;

        public Main() 
        {
            worker = new WorkerWrapperCore("localhost", 5679, KeepAliveHandler, SafeStopeHandler);
        }
        
        public object KeepAliveHandler(ZapJSONMessage message, out bool processing) 
        {
            worker.Trace("KEEP ALIVE RECIVED");
            Console.WriteLine("***** KEEP ALIVE RECIVED *****");
            Thread.Sleep(1000);
            KeepAliveMessage result = new KeepAliveMessage();
            result.ProcessId = Environment.ProcessId.ToString();
            processing = false;
            Console.WriteLine("----- KEEP ALIVE ANSERED -----");
            worker.Trace("KEEP ALIVE ANSERED");
            return result;
        }

        public object? SafeStopeHandler(ZapJSONMessage message, out bool processing)
        {
            worker.Trace("SAFE STOP RECIVED");
            processing = false;
            Environment.Exit(0);    
            return null;
        }
    }
}
