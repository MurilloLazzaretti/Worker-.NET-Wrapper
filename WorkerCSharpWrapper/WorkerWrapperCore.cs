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
        // Trace pelo ZapMQ: as linhas saem em lotes, e o que nao couber e descartado.
        private const int TraceFlushIntervalMs = 250;
        private const int TraceMaxBufferedLines = 5000;
        private const int TraceMaxLinesPerBatch = 500;
        private const int TraceBatchTtlMs = 10000;
        private const int TraceDefaultLeaseSeconds = 30;

        private ZapMQWrapper zapMQ;
        private ZapMQHandler keepAliveHandler;        
        private ZapMQHandler safeStopHandler;

        private string processId;
        private bool traceOnline;
        private Socket? socket;

        private readonly object traceLock = new object();
        private readonly Queue<TraceLine> traceLines = new Queue<TraceLine>();
        private volatile bool traceByZapMQ;
        private string traceQueue = "";
        private DateTime traceLeaseEnd;
        private long traceSequence;
        private long traceDropped;
        private Thread? traceThread;

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
            processing = false;

            var traceMessage = JsonConvert.DeserializeObject<TraceOnlineMessage>(message.Body.ToString()!);

            // Trace pelo ZapMQ, pedido pelo painel. Quem pede renova o pedido de tempos em
            // tempos; sem renovacao o trace se desliga sozinho ao fim do prazo.
            if (traceMessage?.message == "start zapmq trace")
            {
                if (string.IsNullOrEmpty(traceMessage.queue))
                {
                    result.message = "queue is required";
                    return result;
                }
                StartTraceByZapMQ(traceMessage.queue!, traceMessage.lease > 0 ? traceMessage.lease : TraceDefaultLeaseSeconds);
                result.message = "on";
                return result;
            }
            if (traceMessage?.message == "stop zapmq trace")
            {
                StopTraceByZapMQ();
                result.message = "off";
                return result;
            }

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
                    // Shutdown em socket que nao conectou lanca excecao.
                    socket.Close();
                }                               
            }
            else
            {
                traceOnline = false;
                if (socket != null)
                {
                    try
                    {
                        socket.Shutdown(SocketShutdown.Send);
                    }
                    catch
                    {
                        // Ja estava fechado.
                    }
                    result.message = "off";                    
                }
            }            
            return result;
        }

        private void StartTraceByZapMQ(string queue, int leaseSeconds)
        {
            lock (traceLock)
            {
                traceQueue = queue;
                traceLeaseEnd = DateTime.UtcNow.AddSeconds(leaseSeconds);
                if (!traceByZapMQ)
                {
                    traceLines.Clear();
                    traceDropped = 0;
                    traceByZapMQ = true;
                }
                if (traceThread == null)
                {
                    traceThread = new Thread(FlushTrace) { IsBackground = true, Name = "WorkerWrapper trace" };
                    traceThread.Start();
                }
            }
        }

        private void StopTraceByZapMQ()
        {
            lock (traceLock)
            {
                traceByZapMQ = false;
                traceLines.Clear();
            }
        }

        // Unica thread que publica o trace. Fica viva depois do primeiro pedido e nao faz
        // nada enquanto o trace esta desligado.
        private void FlushTrace()
        {
            while (true)
            {
                Thread.Sleep(TraceFlushIntervalMs);

                List<TraceLine> batch;
                string queue;
                long dropped;
                lock (traceLock)
                {
                    if (!traceByZapMQ)
                        continue;
                    if (DateTime.UtcNow > traceLeaseEnd)
                    {
                        traceByZapMQ = false;
                        traceLines.Clear();
                        continue;
                    }
                    if (traceLines.Count == 0 && traceDropped == 0)
                        continue;

                    batch = new List<TraceLine>(Math.Min(traceLines.Count, TraceMaxLinesPerBatch));
                    while (traceLines.Count > 0 && batch.Count < TraceMaxLinesPerBatch)
                        batch.Add(traceLines.Dequeue());
                    queue = traceQueue;
                    dropped = traceDropped;
                    traceDropped = 0;
                }

                bool sent;
                try
                {
                    sent = zapMQ.SendMessage(queue, new TraceBatch(processId, dropped, batch), TraceBatchTtlMs);
                }
                catch
                {
                    sent = false;
                }
                if (!sent)
                {
                    // O trace e descartavel: o que nao foi nao e tentado de novo, so contado.
                    lock (traceLock)
                        traceDropped += dropped + batch.Count;
                }
            }
        }

        // Encerra a conexao do worker com o ZapMQ em ordem. Chamado de dentro do
        // handler de safe stop nao adianta: a mensagem so e confirmada ao servidor
        // depois que o handler retorna. Chame de outra thread, antes de encerrar
        // o processo; a chamada espera essa confirmacao.
        public void Stop()
        {
            traceOnline = false;
            StopTraceByZapMQ();
            zapMQ.StopThreads();
        }

        public async void Trace(string traceText)
        {
            if (traceByZapMQ)
            {
                lock (traceLock)
                {
                    if (traceByZapMQ)
                    {
                        if (traceLines.Count >= TraceMaxBufferedLines)
                        {
                            traceLines.Dequeue();
                            traceDropped++;
                        }
                        traceLines.Enqueue(new TraceLine(++traceSequence, DateTime.UtcNow, traceText));
                    }
                }
            }

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
