using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace VisionQC.LocalAgent.Services
{
    // A slow browser must never hold the inspection thread in a socket write.
    // Preserve FIFO events; disconnect on overflow so the browser reconnects and reconciles SQLite.
    internal sealed class BoundedEventWriter : IDisposable
    {
        private readonly Stream _stream;
        private readonly Action _close;
        private readonly object _sync=new object();
        private readonly Queue<byte[]> _queue=new Queue<byte[]>();
        private readonly SemaphoreSlim _ready=new SemaphoreSlim(0);
        private readonly int _maxCount,_maxBytes;
        private int _bytes,_closed;
        internal BoundedEventWriter(Stream stream,Action close,int maxCount=128,int maxBytes=4*1024*1024)
        {_stream=stream;_close=close;_maxCount=maxCount;_maxBytes=maxBytes;if(stream.CanTimeout)stream.WriteTimeout=2000;Task.Run((Func<Task>)Drain);}
        internal bool TryWrite(byte[] bytes)
        {
            lock(_sync)
            {
                if(_closed!=0)return false;
                if(bytes.Length<=_maxBytes&&_queue.Count<_maxCount&&_bytes<=_maxBytes-bytes.Length)
                {_queue.Enqueue(bytes);_bytes+=bytes.Length;_ready.Release();return true;}
            }
            Dispose();return false;
        }
        private async Task Drain()
        {
            try
            {
                while(true)
                {
                    await _ready.WaitAsync().ConfigureAwait(false);byte[] bytes;
                    lock(_sync){if(_closed!=0)return;bytes=_queue.Dequeue();_bytes-=bytes.Length;}
                    _stream.Write(bytes,0,bytes.Length);
                }
            }
            catch{Dispose();}
        }
        public void Dispose()
        {
            lock(_sync){if(Interlocked.Exchange(ref _closed,1)!=0)return;_queue.Clear();_bytes=0;_ready.Release();}
            try{_close();}catch{}
        }
    }
}
