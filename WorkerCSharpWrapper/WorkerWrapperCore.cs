using System.Diagnostics;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Serialization;
using Newtonsoft.Json;
using ZapMQ;

namespace WorkerCSharpWrapper
{
    public class WorkerWrapperCore
    {
        private ZapMQWrapper zapMQ;
        private ZapMQHandler keepAliveHandler;        
        private ZapMQHandler safeStopHandler;

        private string processId;
        private bool traceOnline;
        private TcpClient? socket;

        public WorkerWrapperCore(string host, int port, ZapMQHandler keepAlive, ZapMQHandler safeStop)
        {
            traceOnline = false;
            zapMQ = new ZapMQWrapper(host, port);            
            processId = Environment.ProcessId.ToString();
            keepAliveHandler = keepAlive;
            safeStopHandler = safeStop;
            BindKeepAliveQueue();
            BindSafeStopQueue();
            BindTraceOnlineQueue();
        }

        private void BindKeepAliveQueue()
        {
            zapMQ.Bind(processId, keepAliveHandler);
        }

        private void BindSafeStopQueue()
        {
            zapMQ.Bind(processId + "SS", safeStopHandler);
        }

        private void BindTraceOnlineQueue()
        {
            zapMQ.Bind(processId + "TR", StartTraceOnline);
        }

        private object StartTraceOnline(ZapJSONMessage message, out bool processing)
        {
            TraceOnlineMessage result = new TraceOnlineMessage("", 0);
            
            var traceMessage = JsonConvert.DeserializeObject<TraceOnlineMessage>(message.Body.ToString()!);
            if (traceMessage?.message == "start trace")
            {
                traceOnline = true;                
                socket = new TcpClient();
                socket.Connect("127.0.0.1", traceMessage.port);
                if (!socket.Connected)
                {
                    traceOnline = false;
                    socket.Close();
                    socket.Dispose();
                    result.message = "cannot connect to the server";
                }
                else
                {
                    result.message = "on";
                }
            }
            else
            {
                traceOnline = false;
                if (socket != null)
                {
                    socket.Close();
                    socket.Dispose();
                    result.message = "off";
                }
            }            
            processing = false;
            return result;
        }

        public void Trace(string traceText)
        {
            if (traceOnline && socket != null && socket.Connected)
            {
                NetworkStream stream = socket.GetStream();
                byte[] bytesToSend = ASCIIEncoding.ASCII.GetBytes(traceText);
                stream.Write(bytesToSend, 0, bytesToSend.Length);
            }
        }
    }
}
