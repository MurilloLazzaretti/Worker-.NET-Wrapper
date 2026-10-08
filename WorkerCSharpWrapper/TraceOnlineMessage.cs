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

        // So no trace pelo ZapMQ ("start zapmq trace"): a fila em que o worker publica
        // as linhas e por quantos segundos, sem novo pedido, ele continua publicando.
        public  string? queue;
        public  int lease;
    }

    // Uma linha de Trace(), com a ordem em que foi escrita e o horario (UTC).
    public class TraceLine(long seq, DateTime at, string text)
    {
        public  long Seq = seq;
        public  DateTime At = at;
        public  string Text = text;
    }

    // O que o worker publica na fila do trace: as linhas desde o ultimo lote e quantas
    // foram descartadas antes delas por falta de espaco ou de conexao.
    public class TraceBatch(string processId, long dropped, List<TraceLine> lines)
    {
        public  string ProcessId = processId;
        public  long Dropped = dropped;
        public  List<TraceLine> Lines = lines;
    }
}
