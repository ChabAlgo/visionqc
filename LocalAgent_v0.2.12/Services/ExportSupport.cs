using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Security.Cryptography;
using System.Data.SQLite;
using System.Threading;
using System.Collections.Generic;
namespace VisionQC.LocalAgent.Services
{
    internal static class ExportLabel
    {
        internal static string Safe(string text,int limit=110) {var invalid=Path.GetInvalidFileNameChars();string value=new string((text??"").Select(c=>invalid.Contains(c)||char.IsControl(c)?'_':c).ToArray()).Trim(' ','.');return value.Length>limit?value.Substring(0,limit):value;}
        internal static string Name(params string[] parts) {return "VisionQC_"+Safe(string.Join("_",parts.Where(x=>!string.IsNullOrWhiteSpace(x))))+"_"+DateTime.Now.ToString("yyyyMMdd_HHmmss")+"_"+Guid.NewGuid().ToString("N").Substring(0,6);}
    }
    // Queue and deduplicate on disk. Never retain all image paths or image bytes in memory.
    internal sealed class ImageCopyPlan : IDisposable
    {
        private readonly SQLiteConnection db;
        private readonly string queue;
        private SQLiteTransaction transaction;
        internal readonly string DirectoryPath;
        private readonly string description;
        internal long Processed,Total,Success,Failed;
        internal ImageCopyPlan(string root,string label)
        {
            description=label;DirectoryPath=Path.Combine(Path.GetFullPath(root),ExportLabel.Name("Images"));Directory.CreateDirectory(DirectoryPath);
            queue=Path.Combine(DirectoryPath,".copy-queue.sqlite");db=new SQLiteConnection("Data Source="+queue+";Version=3;Pooling=False;");db.Open();
            using(var cmd=db.CreateCommand()){cmd.CommandText="PRAGMA journal_mode=OFF;PRAGMA synchronous=OFF;PRAGMA cache_size=-2048;CREATE TABLE files(path TEXT PRIMARY KEY COLLATE NOCASE,position TEXT);";cmd.ExecuteNonQuery();}
            transaction=db.BeginTransaction();
        }
        internal void Add(string path,string position)
        {using(var cmd=db.CreateCommand()){cmd.Transaction=transaction;cmd.CommandText="INSERT OR IGNORE INTO files VALUES(@p,@pos)";cmd.Parameters.AddWithValue("@p",path??"");cmd.Parameters.AddWithValue("@pos",position??"");cmd.ExecuteNonQuery();}}
        internal object Copy(CancellationToken token)
        {
            transaction.Commit();transaction.Dispose();transaction=null;
            using(var cmd=db.CreateCommand()){cmd.CommandText="SELECT COUNT(*) FROM files";Total=Convert.ToInt64(cmd.ExecuteScalar());}
            string report=Path.Combine(DirectoryPath,"Copy_results.csv");bool cancelled=false;
            using(var log=new StreamWriter(report,false,new UTF8Encoding(true)))
            using(var cmd=db.CreateCommand())
            {
                log.WriteLine("Position,Source,Destination,Status,Message,Selection");cmd.CommandText="SELECT path,position FROM files ORDER BY rowid";
                using(var rows=cmd.ExecuteReader())while(rows.Read())
                {
                    if(token.IsCancellationRequested){cancelled=true;break;}
                    string source=rows.GetString(0),position=rows.GetString(1),target="",partial="",status="Copied",message="";
                    try
                    {
                        if(string.IsNullOrWhiteSpace(source)||!Path.IsPathRooted(source))throw new IOException("원본 절대 경로가 없습니다.");
                        source=Path.GetFullPath(source);string parent=Path.GetDirectoryName(source),hash;
                        using(var sha=SHA256.Create())hash=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(parent.ToUpperInvariant()))).Replace("-","").Substring(0,12);
                        string folder=Path.Combine(DirectoryPath,ExportLabel.Safe(position,20),ExportLabel.Safe(new DirectoryInfo(parent).Name,12)+"_"+hash);Directory.CreateDirectory(folder);
                        target=Path.Combine(folder,Path.GetFileName(source));partial=target+".partial";
                        using(var input=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.Read,1048576,FileOptions.SequentialScan))
                        using(var output=new FileStream(partial,FileMode.CreateNew,FileAccess.Write,FileShare.None,1048576,FileOptions.SequentialScan))
                        {byte[] buffer=new byte[1048576];int n;while((n=input.Read(buffer,0,buffer.Length))>0){token.ThrowIfCancellationRequested();output.Write(buffer,0,n);}output.Flush();}
                        File.Move(partial,target);Success++;
                    }
                    catch(OperationCanceledException){if(File.Exists(partial))File.Delete(partial);cancelled=true;status="Cancelled";}
                    catch(Exception ex){if(File.Exists(partial))File.Delete(partial);Failed++;status="Failed";message=ex.Message;}
                    log.WriteLine(ResultCsv.WriteRecord(new[]{position,source,target,status,message,description}));Processed++;
                    if(cancelled)break;
                }
            }
            return new {directory=DirectoryPath,report,total=Total,processed=Processed,copied=Success,failed=Failed,cancelled};
        }
        public void Dispose(){transaction?.Dispose();db.Dispose();if(File.Exists(queue))File.Delete(queue);}
    }
}
