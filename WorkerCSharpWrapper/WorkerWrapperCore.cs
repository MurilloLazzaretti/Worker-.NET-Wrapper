using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
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
            // Environment.ProcessId so existe a partir do .NET 5.
            processId = Process.GetCurrentProcess().Id.ToString();
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

        // Encerra a conexao do worker com o ZapMQ em ordem. Chamado de dentro do
        // handler de safe stop nao adianta: a mensagem so e confirmada ao servidor
        // depois que o handler retorna. Chame de outra thread, antes de encerrar
        // o processo; a chamada espera essa confirmacao.
        public void Stop()
        {
            traceOnline = false;
            zapMQ.StopThreads();
        }

        public async void Trace(string traceText)
        {
            Socket? current = socket;
            if (traceOnline && current != null && current.Connected)
            {
                try
                {
                    var messageBytes = Encoding.UTF8.GetBytes(traceText);
                    // A sobrecarga SendAsync(byte[], SocketFlags) so existe no
                    // .NET Core. O padrao APM abaixo compila nos dois targets.
                    await Task.Factory.FromAsync(
                        (callback, state) => current.BeginSend(messageBytes, 0, messageBytes.Length, SocketFlags.None, callback, state),
                        current.EndSend,
                        null);
                }
                catch
                {
                    traceOnline = false;
                    current.Shutdown(SocketShutdown.Send);
                }
            }
        }
    }
}
