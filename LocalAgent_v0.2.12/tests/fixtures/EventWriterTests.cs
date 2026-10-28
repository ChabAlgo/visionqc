using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using VisionQC.LocalAgent.Services;
class EventWriterTests
{
    sealed class GateStream:MemoryStream
    {
        internal readonly ManualResetEventSlim Entered=new ManualResetEventSlim(),Release=new ManualResetEventSlim();
        public override void Write(byte[] bytes,int offset,int count){Entered.Set();Release.Wait();lock(this)base.Write(bytes,offset,count);}
    }
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    static void Main()
    {
        try
        {
            var slow=new GateStream();int closed=0;
            using(var writer=new BoundedEventWriter(slow,()=>{Interlocked.Increment(ref closed);slow.Release.Set();},4,128))
            {
                writer.TryWrite(new byte[]{1});Check(slow.Entered.Wait(10000),"Consumer did not start");
                var clock=Stopwatch.StartNew();for(int i=0;i<4;i++)Check(writer.TryWrite(new byte[]{(byte)(i+2)}),"Premature overflow");
                Check(clock.ElapsedMilliseconds<500,"Slow stream blocked producer");Check(!writer.TryWrite(new byte[]{9})&&closed==1,"Queue overflow did not close slow client exactly once");
            }
            var ordered=new MemoryStream();using(var writer=new BoundedEventWriter(ordered,()=>{})){for(int i=0;i<50;i++)Check(writer.TryWrite(new[]{(byte)i}),"Unexpected overflow");Check(SpinWait.SpinUntil(()=>{lock(ordered)return ordered.Length==50;},10000),"Events did not drain");Check(ordered.ToArray().SequenceEqual(Enumerable.Range(0,50).Select(i=>(byte)i)),"Event order changed");}
            int oversized=0;using(var writer=new BoundedEventWriter(new MemoryStream(),()=>oversized++,4,32)){Check(!writer.TryWrite(new byte[33])&&oversized==1,"Byte limit was not enforced");}
            Console.WriteLine("PASS bounded event writer: blocked consumer never blocks inspection producer, FIFO, count/byte limits, overflow disconnect, idempotent shutdown.");
        }
        catch(Exception error){Console.WriteLine(error);Environment.Exit(1);}
    }
}
