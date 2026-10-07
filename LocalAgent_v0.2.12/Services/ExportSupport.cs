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
        internal static string Safe(string text,int limit=110) {var invalid=Path.GetInvalidFileNameChars();string value=new string((text??"").Select(c=>invalid.Contains(c)||char.IsControl(c)?'_':c).ToArray()).Trim(' ','.');if(System.Text.RegularExpressions.Regex.IsMatch(value,@"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)",System.Text.RegularExpressions.RegexOptions.IgnoreCase))value="_"+value;return value.Length>limit?value.Substring(0,limit):value;}
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
        private readonly string group;
        internal long Processed,Total,Success,Failed;
        internal ImageCopyPlan(string root,string label,string group=null)
        {
            description=label;this.group=ExportLabel.Safe(group??"정보",80);if(string.IsNullOrWhiteSpace(this.group))this.group="정보";DirectoryPath=Path.GetFullPath(root);Directory.CreateDirectory(DirectoryPath);
            queue=Path.Combine(DirectoryPath,".copy-queue-"+Guid.NewGuid().ToString("N")+".sqlite");db=new SQLiteConnection("Data Source="+queue+";Version=3;Pooling=False;");db.Open();
            using(var cmd=db.CreateCommand()){cmd.CommandText="PRAGMA journal_mode=OFF;PRAGMA synchronous=OFF;PRAGMA cache_size=-2048;CREATE TABLE files(path TEXT COLLATE NOCASE,position TEXT COLLATE NOCASE,PRIMARY KEY(path,position));";cmd.ExecuteNonQuery();}
            transaction=db.BeginTransaction();
        }
        internal void Add(string path,string position)
        {using(var cmd=db.CreateCommand()){cmd.Transaction=transaction;cmd.CommandText="INSERT OR IGNORE INTO files VALUES(@p,@pos)";cmd.Parameters.AddWithValue("@p",path??"");cmd.Parameters.AddWithValue("@pos",position??"");cmd.ExecuteNonQuery();}}
        internal object Copy(CancellationToken token)
        {
            transaction.Commit();transaction.Dispose();transaction=null;
            using(var cmd=db.CreateCommand()){cmd.CommandText="SELECT COUNT(*) FROM files";Total=Convert.ToInt64(cmd.ExecuteScalar());}
            string report=Path.Combine(DirectoryPath,"Copy_results_"+DateTime.Now.ToString("yyyyMMdd_HHmmss")+"_"+Guid.NewGuid().ToString("N").Substring(0,6)+".csv");bool cancelled=false;
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
                        source=Path.GetFullPath(source);
                        using(var input=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.Read,1048576,FileOptions.SequentialScan))
                        {
                            string positionFolder=ExportLabel.Safe(position,80);if(string.IsNullOrWhiteSpace(positionFolder))positionFolder="Position";
                            string folder=Path.Combine(DirectoryPath,positionFolder,group);Directory.CreateDirectory(folder);
                            target=AvailableTarget(folder,source);partial=target+"."+Guid.NewGuid().ToString("N")+".partial";
                            using(var output=new FileStream(partial,FileMode.CreateNew,FileAccess.Write,FileShare.None,1048576,FileOptions.SequentialScan))
                            {byte[] buffer=new byte[1048576];int n;while((n=input.Read(buffer,0,buffer.Length))>0){token.ThrowIfCancellationRequested();output.Write(buffer,0,n);}output.Flush();}
                        }
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
        private static string AvailableTarget(string folder,string source)
        {
            string name=Path.GetFileName(source),target=Path.Combine(folder,name);if(!File.Exists(target)&&!Directory.Exists(target))return target;
            string hash;using(var sha=SHA256.Create())hash=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(source.ToUpperInvariant()))).Replace("-","").Substring(0,12);
            string stem=Path.GetFileNameWithoutExtension(name),extension=Path.GetExtension(name);
            target=Path.Combine(folder,stem+"_"+hash+extension);int index=1;
            while(File.Exists(target)||Directory.Exists(target))target=Path.Combine(folder,stem+"_"+hash+"_"+(index++)+extension);
            return target;
        }
        public void Dispose(){transaction?.Dispose();db.Dispose();if(File.Exists(queue))File.Delete(queue);}
    }
    internal static class SingleImageCopy
    {
        internal static object Copy(string source,string target,bool overwrite)
        {
            if(string.IsNullOrWhiteSpace(source)||string.IsNullOrWhiteSpace(target)||!Path.IsPathRooted(source)||!Path.IsPathRooted(target))throw new IOException("원본과 저장 파일의 절대 경로가 필요합니다.");
            source=Path.GetFullPath(source);target=Path.GetFullPath(target);
            if(string.Equals(source,target,StringComparison.OrdinalIgnoreCase))throw new IOException("원본과 다른 저장 위치를 선택하세요.");
            if(!Directory.Exists(Path.GetDirectoryName(target)))throw new DirectoryNotFoundException("저장 폴더를 찾을 수 없습니다.");
            if(File.Exists(target)&&!overwrite)throw new IOException("같은 이름의 파일이 있습니다. 다른 이름이나 위치를 선택하세요.");
            string partial=target+"."+Guid.NewGuid().ToString("N")+".partial";
            try
            {
                using(var input=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.Read,1048576,FileOptions.SequentialScan))
                using(var output=new FileStream(partial,FileMode.CreateNew,FileAccess.Write,FileShare.None,1048576,FileOptions.SequentialScan))
                {input.CopyTo(output,1048576);output.Flush();}
                if(File.Exists(target)&&overwrite)File.Replace(partial,target,null);else File.Move(partial,target);
                return new {ok=true,path=target};
            }
            finally {if(File.Exists(partial))File.Delete(partial);}
        }
    }

}
