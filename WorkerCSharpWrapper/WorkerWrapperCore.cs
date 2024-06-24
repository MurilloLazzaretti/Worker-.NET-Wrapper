using System.Diagnostics;
using System.Net;
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
        private Socket? socket;

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


                IPEndPoint ipEndPoint = new(IPAddress.Parse("127.0.0.1"), traceMessage.port);
                
                socket = new(
                        ipEndPoint.AddressFamily,
                        SocketType.Stream,
                        ProtocolType.Tcp);
                try
                {
                    socket.Connect(ipEndPoint);
                    if (!socket.Connected)
                    {
                        traceOnline = false;
                        socket.Close();
                        result.message = "cannot connect to the server";
                    }
                    else
                    {
                        result.message = "on";
                    }
                }
                catch(Exception e)
                {
                    result.message = "cannot connect to the server :" + e.Message;
                    traceOnline = false;
                    socket.Shutdown(SocketShutdown.Send);
                }                               
            }
            else
            {
                traceOnline = false;
                if (socket != null)
                {
                    socket.Shutdown(SocketShutdown.Send);
                    result.message = "off";                    
                }
            }            
            processing = false;
            return result;
        }

        public async void Trace(string traceText)
        {
            if (traceOnline && socket != null && socket.Connected)
            {
                try
                {
                    var messageBytes = Encoding.UTF8.GetBytes(traceText);
                    await socket.SendAsync(messageBytes, SocketFlags.None);
                }
                catch
                {
                    traceOnline = false;
                    socket.Shutdown(SocketShutdown.Send);
                }
            }
        }
    }
}
