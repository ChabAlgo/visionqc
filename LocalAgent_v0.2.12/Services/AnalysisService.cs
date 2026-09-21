using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using VisionQC.LocalAgent.Persistence;

namespace VisionQC.LocalAgent.Services
{
    // Temporary analysis is separate from saved inspection history, including after restart.
    // HTTP returns job references immediately; importing/projecting never holds an HTTP request open.
    internal sealed class AnalysisService : IDisposable
    {
        private readonly string _root;
        private readonly object _sync = new object();
        private readonly CancellationTokenSource _stop = new CancellationTokenSource();
        private Job _job;
        private sealed class Job
        {
            internal string Id, RunId, SourcePath, Error;
            internal long Imported, Projected;
            internal volatile bool Running, Completed;
            internal CancellationTokenSource Cancellation;
            internal AnalysisProjection Projection;
            internal Task Task;
            internal string OperationId;
            internal object Result;
        }
        internal sealed class Request
        {
            public string analysisId { get; set; }
            public string[] filePaths { get; set; }
            public Dictionary<string,string> filePositions {get;set;}
            public string[] excludedPositions {get;set;}
            public AgentHistoryFileImportRequest options { get; set; }
            public string date { get; set; }
            public int dateOffset { get; set; }
            public string tool {get;set;}
            public string position {get;set;}
            public string scope {get;set;}="TOOL_NG";
            public double afterScore {get;set;}=-1;
            public long afterId {get;set;}
            public int pageSize {get;set;}=200;
            public string day {get;set;}
            public string cellId {get;set;}
            public string afterDay {get;set;}
            public string afterCell {get;set;}
            public AnalysisProjection.ThresholdValue[] thresholds {get;set;}
            public bool excludeOtherToolNg {get;set;}
            public double exclusionThreshold {get;set;}=.8;
            public bool previous {get;set;}
            public string batchId {get;set;}
            public string action {get;set;}
            public AnalysisProjection.ActualNgValue[] actualNg {get;set;}
            public string outputDirectory {get;set;}
            public string kind {get;set;}="all";
            public string compare {get;set;}="lte";
            public double cutoff {get;set;}=.5;
            public long maxRows {get;set;}=1000000;
            public bool splitByDate {get;set;}
            public int dateStart {get;set;}=-1;
            public string dateAnchor {get;set;}
        }
        internal AnalysisService(string root) { _root = Path.GetFullPath(root); Directory.CreateDirectory(_root); }

        internal object StartImport(string body)
        {
            var request = new JavaScriptSerializer().Deserialize<Request>(body ?? "{}");
            if (request == null || request.filePaths == null || request.filePaths.Length == 0 || request.filePaths.Length > 10000)
                return new { ok=false,error="CSV 파일을 선택하세요. 한 번에 최대 10,000개입니다." };
            lock (_sync)
            {
                if (_job != null && _job.Running) return new { ok=false,error="진행 중인 분석 입력을 완료하거나 취소하세요." };
                if (_job != null && _job.Projection != null) _job.Projection.Dispose();
                if (_job != null && _job.Cancellation != null) _job.Cancellation.Dispose();
                var id=Guid.NewGuid().ToString("N");
                var directory=Path.Combine(_root,id); Directory.CreateDirectory(directory);
                var job=new Job {Id=id,SourcePath=Path.Combine(directory,"source.sqlite"),Running=true,Cancellation=CancellationTokenSource.CreateLinkedTokenSource(_stop.Token)};
                _job=job;
                job.Task=Task.Run(() => {
                    try
                    {
                        using(var store=new SqliteRunStore(job.SourcePath))
                        {
                            var importer=new CsvHistoryFileImporter(store,new JavaScriptSerializer());
                            string runId;
                            var filePositions=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
                            if(request.filePositions!=null)foreach(var pair in request.filePositions)filePositions[Path.GetFullPath(pair.Key)]=pair.Value;
                            importer.ImportFiles(request.options,request.filePaths,n=>Interlocked.Exchange(ref job.Imported,n),job.Cancellation.Token,out runId,filePositions,new HashSet<string>(request.excludedPositions??new string[0],StringComparer.OrdinalIgnoreCase));
                            job.RunId=runId;
                            File.WriteAllText(Path.Combine(directory,"run.txt"),runId);
                            job.Projection=new AnalysisProjection(job.SourcePath,Path.Combine(directory,"projection.sqlite"),runId);
                            while(job.Projection.Advance(job.Cancellation.Token,10000)) Interlocked.Exchange(ref job.Projected,job.Projection.Cursor);
                            job.Completed=true;
                        }
                    }
                    catch(OperationCanceledException) { job.Error="분석 입력을 취소했습니다."; }
                    catch(Exception ex) { job.Error=ex.Message; }
                    finally { job.Running=false; }
                });
                return StatusObject(job);
            }
        }

        internal object Status(string body)
        {
            var request=new JavaScriptSerializer().Deserialize<Request>(body ?? "{}");
            lock(_sync)
            {
                var job=Resolve(request == null ? null : request.analysisId);
                return StatusObject(job);
            }
        }
        internal object Summary(string body)
        {
            var request=new JavaScriptSerializer().Deserialize<Request>(body ?? "{}");
            lock(_sync)
            {
                var job=Resolve(request == null ? null : request.analysisId);
                if(!job.Completed || job.Projection==null) return StatusObject(job);
                return new {ok=true,analysisId=job.Id,runId=job.RunId,summary=job.Projection.Summary(request.date,request.dateOffset)};
            }
        }
        internal object Scores(string body)
        {
            var request=new JavaScriptSerializer().Deserialize<Request>(body ?? "{}");
            lock(_sync)
            {
                var job=Resolve(request==null?null:request.analysisId);
                if(!job.Completed || job.Projection==null)return StatusObject(job);
                return new {ok=true,analysisId=job.Id,page=job.Projection.ScorePage(request.tool,request.position,request.scope,request.afterScore,request.afterId,request.pageSize)};
            }
        }
        internal object ScoreWindow(string body)
        {
            var request=new JavaScriptSerializer().Deserialize<Request>(body??"{}")??new Request();
            lock(_sync)
            {
                var job=Resolve(request.analysisId);if(job.Running||!job.Completed)return StatusObject(job);
                return new {ok=true,analysisId=job.Id,page=job.Projection.ScoreWindow(request.tool,request.position,request.scope,request.excludeOtherToolNg,request.exclusionThreshold,request.afterScore,request.afterId,request.pageSize,request.previous,job.Cancellation.Token)};
            }
        }
        internal object Statistics(string body)
        {
            var request=new JavaScriptSerializer().Deserialize<Request>(body??"{}")??new Request();
            lock(_sync)
            {
                var job=Resolve(request.analysisId);if(job.Running)return new {ok=false,busy=true,error="분석 갱신 중입니다."};
                StartOperation(job,()=>job.Result=job.Projection.ScoreStatistics(request.tool,request.position,request.scope,request.excludeOtherToolNg,request.exclusionThreshold,job.Cancellation.Token));
                return StatusObject(job);
            }
        }
        internal object Dashboard(string body)
        {
            var request=new JavaScriptSerializer().Deserialize<Request>(body??"{}")??new Request();
            lock(_sync)
            {
                var job=Resolve(request.analysisId);if(job.Running)return new {ok=false,busy=true,error="분석 갱신 중입니다."};
                StartOperation(job,()=>job.Result=job.Projection.Dashboard(request.date,request.exclusionThreshold,job.Cancellation.Token));
                return StatusObject(job);
            }
        }
        internal object Export(string body)
        {
            var request=new JavaScriptSerializer().Deserialize<Request>(body??"{}")??new Request();
            if(request.maxRows<1||string.IsNullOrWhiteSpace(request.outputDirectory))throw new InvalidDataException("출력 폴더와 CSV 분할 행 수를 확인하세요.");
            lock(_sync)
            {
                var job=Resolve(request.analysisId);if(job.Running)return new {ok=false,busy=true,error="분석 갱신 중입니다."};
                if(request.kind!="all"&&request.kind!="score"&&request.kind!="misses")throw new InvalidDataException("지원하지 않는 내보내기 형식입니다.");
                StartOperation(job,()=>job.Result=request.kind=="all"
                    ?job.Projection.ExportRaw(request.outputDirectory,request.maxRows,request.splitByDate,job.Cancellation.Token,null)
                    :job.Projection.ExportFiltered(request.outputDirectory,request.maxRows,request.splitByDate,request.kind,request.date,request.position,request.tool,request.scope,request.excludeOtherToolNg,request.exclusionThreshold,request.compare,request.cutoff,job.Cancellation.Token));return StatusObject(job);
            }
        }
        internal object SaveHistory(string body,SqliteRunStore history)
        {
            var request=new JavaScriptSerializer().Deserialize<Request>(body??"{}")??new Request();
            lock(_sync)
            {
                var job=Resolve(request.analysisId);if(job.Running||!job.Completed)return new {ok=false,busy=true,error="분석 갱신 중입니다."};
                StartOperation(job,()=>{history.CopyCompletedRunFrom(job.SourcePath,job.RunId);job.Result=new {saved=true,runId=job.RunId};});return StatusObject(job);
            }
        }
        internal object RemovePosition(string body)
        {
            var request=new JavaScriptSerializer().Deserialize<Request>(body??"{}")??new Request();
            if(string.IsNullOrWhiteSpace(request.position))throw new InvalidDataException("Position을 선택하세요.");
            lock(_sync)
            {
                var prior=Resolve(request.analysisId);if(prior.Running||!prior.Completed)return new {ok=false,busy=true,error="분석 갱신 중입니다."};
                string id=Guid.NewGuid().ToString("N"),directory=Path.Combine(_root,id);Directory.CreateDirectory(directory);
                var job=new Job{Id=id,SourcePath=Path.Combine(directory,"source.sqlite")};
                prior.Projection.Dispose();prior.Cancellation.Dispose();_job=job;
                StartOperation(job,()=>{
                    using(var store=new SqliteRunStore(job.SourcePath))
                    {
                        var session=store.Start(new SqliteRunStore.RunStoreStart{SourceType="analysis-subset",SourceName="Position selection"});
                        try{store.CopyAnalysisRows(session,prior.SourcePath,prior.RunId,request.position);store.Complete(session,"completed","");}
                        catch{store.DiscardFailedImport(session);throw;}
                        job.RunId=session.RunId;File.WriteAllText(Path.Combine(directory,"run.txt"),job.RunId);
                        job.Projection=new AnalysisProjection(job.SourcePath,Path.Combine(directory,"projection.sqlite"),job.RunId);
                        while(job.Projection.Advance(job.Cancellation.Token,10000))Interlocked.Exchange(ref job.Projected,job.Projection.Cursor);
                    }
                });return StatusObject(job);
            }
        }
        internal object Cancel(string body)
        {
            var request=new JavaScriptSerializer().Deserialize<Request>(body ?? "{}");
            lock(_sync) { var job=Resolve(request==null ? null : request.analysisId); if(job.Running) job.Cancellation.Cancel(); return StatusObject(job); }
        }
        internal object Misses(string body)
        {
            var request=new JavaScriptSerializer().Deserialize<Request>(body??"{}")??new Request();
            lock(_sync)
            {
                var job=Resolve(request.analysisId);if(job.Running||!job.Completed)return StatusObject(job);
                return new {ok=true,analysisId=job.Id,page=job.Projection.MissPage(request.date,request.position,request.afterDay,request.afterCell,request.pageSize,job.Cancellation.Token)};
            }
        }
        internal object Dates(string body)
        {
            var request=new JavaScriptSerializer().Deserialize<Request>(body??"{}")??new Request();
            lock(_sync)
            {
                var job=Resolve(request.analysisId);if(job.Running||!job.Completed)return StatusObject(job);
                return new {ok=true,analysisId=job.Id,page=job.Projection.DateWindow(request.dateStart,request.dateAnchor,job.Cancellation.Token)};
            }
        }
        internal object ActualNg(string body)
        {
            var request=new JavaScriptSerializer().Deserialize<Request>(body??"{}")??new Request();
            if(request.actualNg!=null&&request.actualNg.Length>1000)throw new InvalidDataException("실제 NG 입력은 요청당 최대 1,000개입니다.");
            lock(_sync)
            {
                var job=Resolve(request.analysisId);if(job.Running||!job.Completed)return new {ok=false,busy=true,error="분석 갱신 중입니다."};
                job.Projection.StageActualNg(request.batchId,request.action,request.actualNg,job.Cancellation.Token);
                return new {ok=true,analysisId=job.Id,batchId=request.batchId};
            }
        }
        internal object Detail(string body)
        {
            var request=new JavaScriptSerializer().Deserialize<Request>(body??"{}")??new Request();
            lock(_sync)
            {
                var job=Resolve(request.analysisId);if(job.Running||!job.Completed)return StatusObject(job);
                return new {ok=true,analysisId=job.Id,page=job.Projection.CellDetail(request.day,request.cellId,request.position,request.afterId,request.pageSize,job.Cancellation.Token)};
            }
        }
        internal object Thresholds(string body)
        {
            var request=new JavaScriptSerializer().Deserialize<Request>(body??"{}")??new Request();
            if(request.thresholds==null||request.thresholds.Length>10000)throw new InvalidDataException("Threshold 설정이 올바르지 않습니다.");
            lock(_sync)
            {
                var job=Resolve(request.analysisId);if(job.Running)return new {ok=false,busy=true,error="분석 갱신 중입니다."};
                StartOperation(job,()=>job.Projection.SetThresholds(request.thresholds,job.Cancellation.Token));return StatusObject(job);
            }
        }
        // Only the server can supply a live source path. Clients keep an opaque analysis ID.
        internal object BindSource(string sourcePath,string runId)
        {
            lock(_sync)
            {
                if(_job!=null && _job.RunId==runId && string.Equals(_job.SourcePath,sourcePath,StringComparison.OrdinalIgnoreCase))
                {
                    if(!_job.Running)StartOperation(_job,()=>Advance(_job));
                    return StatusObject(_job);
                }
                if(_job!=null&&_job.Running)return new {ok=false,busy=true,error="분석 갱신 중입니다."};
                string id=Guid.NewGuid().ToString("N"),directory=Path.Combine(_root,id);Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory,"source.txt"),Path.GetFullPath(sourcePath));
                File.WriteAllText(Path.Combine(directory,"run.txt"),runId);
                var job=new Job{Id=id,RunId=runId,SourcePath=sourcePath,Cancellation=CancellationTokenSource.CreateLinkedTokenSource(_stop.Token)};
                job.Projection=new AnalysisProjection(sourcePath,Path.Combine(directory,"projection.sqlite"),runId);
                if(_job!=null){_job.Projection?.Dispose();_job.Cancellation?.Dispose();}
                _job=job;StartOperation(job,()=>Advance(job));return StatusObject(job);
            }
        }
        private static void Advance(Job job)
        { while(job.Projection.Advance(job.Cancellation.Token))Interlocked.Exchange(ref job.Projected,job.Projection.Cursor); }
        private void StartOperation(Job job,Action action)
        {
            job.Cancellation?.Dispose();job.Cancellation=CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
            job.Running=true;job.Completed=false;job.Error=null;
            job.OperationId=Guid.NewGuid().ToString("N");job.Result=null;
            job.Task=Task.Run(()=>{try{action();job.Completed=true;}catch(OperationCanceledException){job.Error="분석 갱신을 취소했습니다.";}catch(Exception ex){job.Error=ex.Message;}finally{job.Running=false;}});
        }
        private Job Resolve(string id)
        {
            Guid parsed;
            if(!Guid.TryParseExact(id,"N",out parsed)) throw new InvalidDataException("유효하지 않은 분석 ID입니다.");
            if(_job!=null && _job.Id==id) return _job;
            if(_job!=null && _job.Running) throw new InvalidOperationException("다른 분석을 입력 중입니다.");
            string directory=Path.Combine(_root,parsed.ToString("N")), runFile=Path.Combine(directory,"run.txt");
            if(!File.Exists(runFile)) throw new FileNotFoundException("저장된 분석을 찾을 수 없습니다.");
            string runId=File.ReadAllText(runFile).Trim();
            var job=new Job {Id=id,RunId=runId,SourcePath=Path.Combine(directory,"source.sqlite"),Cancellation=CancellationTokenSource.CreateLinkedTokenSource(_stop.Token)};
            string sourceReference=Path.Combine(directory,"source.txt");
            if(File.Exists(sourceReference))job.SourcePath=File.ReadAllText(sourceReference).Trim();
            if(_job!=null && _job.Projection!=null) _job.Projection.Dispose();
            if(_job!=null && _job.Cancellation!=null) _job.Cancellation.Dispose();
            // Recovery completes an interrupted projection before exposing final statistics.
            job.Running=true; _job=job;
            job.Task=Task.Run(()=>{
                try { job.Projection=new AnalysisProjection(job.SourcePath,Path.Combine(directory,"projection.sqlite"),runId); while(job.Projection.Advance(job.Cancellation.Token)) Interlocked.Exchange(ref job.Projected,job.Projection.Cursor); job.Completed=true; }
                catch(Exception ex) {job.Error=ex.Message;}
                finally {job.Running=false;}
            });
            return job;
        }
        private static object StatusObject(Job job)
        { return new {ok=string.IsNullOrEmpty(job.Error),analysisId=job.Id,runId=job.RunId,running=job.Running,completed=job.Completed,imported=Interlocked.Read(ref job.Imported),projectedCursor=Interlocked.Read(ref job.Projected),error=job.Error,operationId=job.OperationId,result=job.Completed?job.Result:null}; }
        public void Dispose()
        {
            _stop.Cancel();
            Job job; lock(_sync) job=_job;
            if(job!=null && job.Task!=null) { try {job.Task.Wait();} catch(AggregateException){} }
            if(job!=null && job.Projection!=null) job.Projection.Dispose();
            if(job!=null && job.Cancellation!=null) job.Cancellation.Dispose();
            _stop.Dispose();
        }
    }
}
