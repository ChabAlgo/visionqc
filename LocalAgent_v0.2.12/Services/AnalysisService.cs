using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        private readonly Dictionary<string,ExportJob> _exports = new Dictionary<string,ExportJob>();
        internal Action<string> ExportCopyStarting {get;set;}
        internal bool IsBusy { get { lock(_sync) return (_job!=null && _job.Running) || _exports.Values.Any(item=>item.Running); } }
        private sealed class ExportJob
        {
            internal string Id,AnalysisId,Error,Phase="preparing";
            internal volatile bool Running=true,Completed;
            internal CancellationTokenSource Cancellation;
            internal ImageCopyPlan Copy;
            internal Task Task;
            internal object Result;
        }
        private sealed class Job
        {
            internal string Id, RunId, SourcePath, Error;
            internal long Imported, Projected;
            internal volatile bool Running, Completed;
            internal CancellationTokenSource Cancellation;
            internal AnalysisProjection Projection;
            internal Task Task;
            internal string OperationId;
            internal string ImportRequestId,ImportPosition;
            internal bool ImportCommitted;
            internal object Result;
            internal ImageCopyPlan Copy;
        }
        internal sealed class Request
        {
            public string importRequestId {get;set;}
            public string analysisId { get; set; }
            public List<PositionAliasDefinition> positionDefinitions {get;set;}
            public string[] filePaths { get; set; }
            public Dictionary<string,string> filePositions {get;set;}
            public CsvHistoryFileImporter.FileAssignment[] fileAssignments {get;set;}
            public string[] excludedPositions {get;set;}
            public AgentHistoryFileImportRequest options { get; set; }
            public string date { get; set; }
            public int dateOffset { get; set; }
            public string tool {get;set;}
            public string position {get;set;}
            public string[] positions {get;set;}
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
            public bool copyImages {get;set;}
            public bool independent {get;set;}
            public string exportId {get;set;}
            public string exportLabel {get;set;}
            public string imageGroup {get;set;}
            public string kind {get;set;}="all";
            public string compare {get;set;}="lte";
            public double cutoff {get;set;}=.5;
            public long maxRows {get;set;}=1000000;
            public bool splitByDate {get;set;}
            public int dateStart {get;set;}=-1;
            public string dateAnchor {get;set;}
            public bool allDates {get;set;}
        }
        internal AnalysisService(string root) { _root = Path.GetFullPath(root); Directory.CreateDirectory(_root); }

        internal object StartImport(string body)
        {
            var request = new JavaScriptSerializer().Deserialize<Request>(body ?? "{}");
            if (request == null || request.filePaths == null || request.filePaths.Length == 0 || request.filePaths.Length > 10000)
                return new { ok=false,error="CSV/XLSX 파일을 선택하세요. 한 번에 최대 10,000개입니다." };
            if(request.fileAssignments!=null&&request.fileAssignments.Length>10000)return new {ok=false,error="한 번에 최대 10,000개의 파일/Position 조합을 지원합니다."};
            Guid requestToken;if(!string.IsNullOrEmpty(request.importRequestId)&&!Guid.TryParseExact(request.importRequestId,"N",out requestToken))return new{ok=false,error="유효하지 않은 입력 요청 ID입니다."};
            lock (_sync)
            {
                if(!string.IsNullOrEmpty(request.importRequestId)&&_job?.ImportRequestId==request.importRequestId&&(request.analysisId==_job.Id||string.IsNullOrEmpty(request.analysisId)&&_job.Id==request.importRequestId))return StatusObject(_job);
                if (_job != null && _job.Running) return new { ok=false,error="진행 중인 분석 입력을 완료하거나 취소하세요." };
                if(!string.IsNullOrEmpty(request.analysisId))return StartPositionImport(request);
                var id=string.IsNullOrEmpty(request.importRequestId)?Guid.NewGuid().ToString("N"):request.importRequestId;
                if(Directory.Exists(Path.Combine(_root,id)))return StatusObject(Resolve(id));
                if (_job != null && _job.Projection != null) _job.Projection.Dispose();
                if (_job != null && _job.Cancellation != null) _job.Cancellation.Dispose();
                var directory=Path.Combine(_root,id); Directory.CreateDirectory(directory);
                var job=new Job {Id=id,SourcePath=Path.Combine(directory,"source.sqlite"),Running=true,Cancellation=CancellationTokenSource.CreateLinkedTokenSource(_stop.Token)};
                RecordImportRequest(job,id,null);job.OperationId=id;
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
                            importer.ImportFiles(request.options,request.filePaths,n=>Interlocked.Exchange(ref job.Imported,n),job.Cancellation.Token,out runId,filePositions,new HashSet<string>(request.excludedPositions??new string[0],StringComparer.OrdinalIgnoreCase),request.fileAssignments);
                            job.RunId=runId;
                            File.WriteAllText(Path.Combine(directory,"run.txt"),runId);
                            job.Projection=new AnalysisProjection(job.SourcePath,Path.Combine(directory,"projection.sqlite"),runId);
                            // Configure aliases while facts are still empty, before the single projection pass.
                            job.Projection.ConfigurePositions(request.options?.positionDefinitions,job.Cancellation.Token,false);
                            while(job.Projection.Advance(job.Cancellation.Token,10000)) Interlocked.Exchange(ref job.Projected,job.Projection.Cursor);
                            CompleteInitialImport(job);job.Completed=true;
                        }
                    }
                    catch(OperationCanceledException) { job.Error="분석 입력을 취소했습니다."; }
                    catch(Exception ex) { job.Error=ex.Message; }
                    finally { job.Running=false; }
                });
                return StatusObject(job);
            }
        }

        private object StartPositionImport(Request request,bool remove=false)
        {
            var job=Resolve(request.analysisId);
            if(job.Running||job.Projection==null)return new{ok=false,error="분석 복원이 끝난 뒤 다시 선택하세요."};
            if(!string.IsNullOrEmpty(request.importRequestId)&&job.ImportRequestId==request.importRequestId)return StatusObject(job);
            if(string.IsNullOrWhiteSpace(request.position)||(!remove&&(request.fileAssignments==null||request.fileAssignments.Length==0||request.fileAssignments.Any(a=>a==null||a.position!=request.position))))
                return new{ok=false,error="변경할 Position과 결과 파일의 지정을 확인하세요."};
            if(File.Exists(Path.Combine(_root,job.Id,"source.txt")))return new{ok=false,error="실행 중인 원본은 교체할 수 없습니다. 새 분석으로 불러오세요."};
            if(!job.Projection.HasPositionConfiguration(request.options?.positionDefinitions))return new{ok=false,error="Position 설정을 적용한 뒤 다시 선택하세요."};
            if(_exports.Values.Any(item=>item.Running&&item.Phase=="preparing"))return new{ok=false,error="CSV·이미지 목록 준비가 끝난 뒤 결과 파일을 교체하세요."};
            RecordImportRequest(job,string.IsNullOrEmpty(request.importRequestId)?Guid.NewGuid().ToString("N"):request.importRequestId,request.position);
            job.Imported=0;job.Projected=0;
            StartOperation(job,()=>{
                string staging=Path.Combine(_root,job.Id,"import-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(staging);
                try
                {
                    string source=Path.Combine(staging,"source.sqlite"),cache=Path.Combine(staging,"projection.sqlite"),runId;
                    using(var store=new SqliteRunStore(source))
                    {
                        if(remove){var session=store.Start(new SqliteRunStore.RunStoreStart{SourceType="csv-file-stream",SourceName="Position removal"});runId=session.RunId;store.Complete(session,"completed","");}
                        else{var importer=new CsvHistoryFileImporter(store,new JavaScriptSerializer());importer.ImportFiles(request.options,request.filePaths,n=>Interlocked.Exchange(ref job.Imported,n),job.Cancellation.Token,out runId,null,null,request.fileAssignments);}
                        using(var projection=new AnalysisProjection(source,cache,runId))
                        {
                            projection.ConfigurePositions(request.options?.positionDefinitions,job.Cancellation.Token,false);
                            while(projection.Advance(job.Cancellation.Token,10000))Interlocked.Exchange(ref job.Projected,projection.Cursor);
                        }
                    }
                    job.Projection.ReplacePosition(request.position,source,cache,job.Cancellation.Token,job.ImportRequestId);job.ImportCommitted=true;
                }
                finally
                {
                    // Only this operation's private staging files; user CSV/XLSX/images are untouched.
                    try{Directory.Delete(staging,true);}catch(IOException){}catch(UnauthorizedAccessException){}
                }
            },job.ImportRequestId);
            return StatusObject(job);
        }

        private sealed class ImportRequestMarker {public string id {get;set;} public string position {get;set;} }
        private void RecordImportRequest(Job job,string requestId,string position)
        {
            string path=Path.Combine(_root,job.Id,"import-request.json"),staging=path+".tmp";
            string content=new JavaScriptSerializer().Serialize(new ImportRequestMarker{id=requestId,position=position});
            for(int attempt=0;;attempt++)
            {
                try{File.WriteAllText(staging,content);if(File.Exists(path))File.Replace(staging,path,null);else File.Move(staging,path);break;}
                catch(IOException)when(attempt<5){Thread.Sleep(30);}
            }
            job.ImportRequestId=requestId;job.ImportPosition=position;job.ImportCommitted=false;
        }
        private void CompleteInitialImport(Job job)
        {
            job.Cancellation.Token.ThrowIfCancellationRequested();
            File.WriteAllText(Path.Combine(_root,job.Id,"import-committed.txt"),job.ImportRequestId);
            job.ImportCommitted=true;
        }

        internal object Status(string body)
        {
            var request=new JavaScriptSerializer().Deserialize<Request>(body ?? "{}");
            lock(_sync)
            {
                var job=Resolve(request == null ? null : request.analysisId);
                if(request?.action=="restore"&&!job.Running&&job.Projection!=null&&(!job.Completed||!string.IsNullOrEmpty(job.Error)))StartOperation(job,()=>Advance(job));
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
        internal object ToolImages(string body)
        {
            var request=new JavaScriptSerializer().Deserialize<Request>(body??"{}")??new Request();
            lock(_sync)
            {
                var job=Resolve(request.analysisId);if(job.Running||!job.Completed)return new {ok=false,busy=true,error="분석 갱신이 끝난 뒤 다시 조회하세요."};
                return new {ok=true,analysisId=job.Id,page=job.Projection.ToolImages(request.date,request.position,request.tool,request.afterId,request.pageSize,request.previous,job.Cancellation.Token)};
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
                StartOperation(job,()=>{job.Projection.ConfigurePositions(request.positionDefinitions,job.Cancellation.Token);job.Result=job.Projection.Dashboard(request.date,request.exclusionThreshold,job.Cancellation.Token);});
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
                if(request.kind!="all"&&request.kind!="score"&&request.kind!="misses"&&request.kind!="selection"&&request.kind!="tool-ng")throw new InvalidDataException("지원하지 않는 내보내기 형식입니다.");
                if(request.kind=="tool-ng"&&(string.IsNullOrWhiteSpace(request.tool)||string.IsNullOrWhiteSpace(request.position)))throw new InvalidDataException("Position과 Tool을 선택하세요.");
                if(_exports.Values.Any(item=>item.Running))return new {ok=false,busy=true,error="진행 중인 CSV·이미지 저장을 완료하거나 취소하세요."};
                if(request.independent)return StartIndependentExport(job,request);
                StartOperation(job,()=>{
                    string name=ExportLabel.Name(request.exportLabel??string.Join("_",new[]{request.position,request.tool,request.kind,request.date}));
                    using(var plan=request.copyImages?new ImageCopyPlan(request.outputDirectory,request.exportLabel??request.kind,request.imageGroup??(request.kind=="misses"?"미검Cell":(request.kind=="score"||request.kind=="tool-ng"?request.tool:"정보"))):null)
                    {
                        job.Copy=plan;Action<string,string> add=plan==null?(Action<string,string>)null:plan.Add;
                        object result=request.kind=="selection"||request.kind=="tool-ng"
                            ?job.Projection.ExportSelection(request.outputDirectory,request.maxRows,request.splitByDate,request.date,request.kind=="tool-ng"?new[]{request.position}:request.positions,request.kind=="tool-ng"?request.tool:"",job.Cancellation.Token,name,add)
                            :request.kind=="all"?job.Projection.ExportRaw(request.outputDirectory,request.maxRows,request.splitByDate,job.Cancellation.Token,null,name,add)
                            :job.Projection.ExportFiltered(request.outputDirectory,request.maxRows,request.splitByDate,request.kind,request.date,request.position,request.tool,request.scope,request.excludeOtherToolNg,request.exclusionThreshold,request.compare,request.cutoff,job.Cancellation.Token,name,add);
                        var json=new JavaScriptSerializer();var data=json.Deserialize<Dictionary<string,object>>(json.Serialize(result));
                        if(plan!=null)data["images"]=plan.Copy(job.Cancellation.Token);
                        job.Result=data;
                    }
                });return StatusObject(job);
            }
        }

        private object StartIndependentExport(Job job,Request request)
        {
            if(!job.Completed||job.Projection==null)return new {ok=false,busy=true,error="분석 갱신 완료 후 저장하세요."};
            if(_exports.Values.Any(item=>item.Running))return new {ok=false,busy=true,error="진행 중인 CSV·이미지 저장을 완료하거나 취소하세요."};
            foreach(string id in _exports.Keys.Where(id=>!_exports[id].Running).Take(Math.Max(0,_exports.Count-7)).ToArray())
            { _exports[id].Cancellation.Dispose();_exports.Remove(id); }
            var snapshot=job.Projection.OpenExportReader();
            var export=new ExportJob{Id=Guid.NewGuid().ToString("N"),AnalysisId=job.Id,Cancellation=CancellationTokenSource.CreateLinkedTokenSource(_stop.Token)};
            _exports.Add(export.Id,export);
            export.Task=Task.Run(()=>{
                try
                {
                    string name=ExportLabel.Name(request.exportLabel??string.Join("_",new[]{request.position,request.tool,request.kind,request.date}));
                    using(var plan=request.copyImages?new ImageCopyPlan(request.outputDirectory,request.exportLabel??request.kind,request.imageGroup??(request.kind=="misses"?"미검Cell":(request.kind=="score"||request.kind=="tool-ng"?request.tool:"정보")),Path.Combine(_root,"export-work")):null)
                    {
                        export.Copy=plan;Action<string,string> add=plan==null?(Action<string,string>)null:plan.Add;object result;
                        using(snapshot)
                        {
                            result=request.kind=="selection"||request.kind=="tool-ng"
                                ?snapshot.ExportSelection(request.outputDirectory,request.maxRows,request.splitByDate,request.date,request.kind=="tool-ng"?new[]{request.position}:request.positions,request.kind=="tool-ng"?request.tool:"",export.Cancellation.Token,name,add)
                                :request.kind=="all"?snapshot.ExportRaw(request.outputDirectory,request.maxRows,request.splitByDate,export.Cancellation.Token,null,name,add)
                                :snapshot.ExportFiltered(request.outputDirectory,request.maxRows,request.splitByDate,request.kind,request.date,request.position,request.tool,request.scope,request.excludeOtherToolNg,request.exclusionThreshold,request.compare,request.cutoff,export.Cancellation.Token,name,add);
                        }
                        // Release the DB snapshot before potentially slow network image transfer.
                        var json=new JavaScriptSerializer();var data=json.Deserialize<Dictionary<string,object>>(json.Serialize(result));
                        if(plan!=null){export.Phase="copying";ExportCopyStarting?.Invoke(export.Id);data["images"]=plan.Copy(export.Cancellation.Token);}
                        export.Result=data;export.Completed=true;export.Phase="completed";
                    }
                }
                catch(OperationCanceledException){export.Error="CSV·이미지 저장을 취소했습니다.";export.Phase="cancelled";}
                catch(Exception error){export.Error=error.Message;export.Phase="failed";}
                finally{snapshot.Dispose();export.Running=false;}
            });
            return ExportStatusObject(export);
        }
        internal object ExportStatus(string body,bool cancel=false)
        {
            var request=new JavaScriptSerializer().Deserialize<Request>(body??"{}")??new Request();
            lock(_sync)
            {
                ExportJob export;
                if(string.IsNullOrEmpty(request.exportId)||!_exports.TryGetValue(request.exportId,out export)||(!string.IsNullOrEmpty(request.analysisId)&&request.analysisId!=export.AnalysisId))throw new InvalidDataException("저장 작업을 찾을 수 없습니다.");
                if(cancel&&export.Running)export.Cancellation.Cancel();
                return ExportStatusObject(export);
            }
        }
        private static object ExportStatusObject(ExportJob job)
        { var copy=job.Copy;return new{ok=string.IsNullOrEmpty(job.Error),analysisId=job.AnalysisId,exportId=job.Id,operationId=job.Id,running=job.Running,completed=job.Completed,phase=job.Phase,copyPrepared=copy==null?0:Interlocked.Read(ref copy.Prepared),copyProcessed=copy==null?0:Interlocked.Read(ref copy.Processed),copyTotal=copy==null?0:Interlocked.Read(ref copy.Total),error=job.Error,result=job.Completed?job.Result:null}; }
        internal object SaveHistory(string body,SqliteRunStore history)
        {
            var request=new JavaScriptSerializer().Deserialize<Request>(body??"{}")??new Request();
            lock(_sync)
            {
                var job=Resolve(request.analysisId);if(job.Running||!job.Completed)return new {ok=false,busy=true,error="분석 갱신 중입니다."};
                StartOperation(job,()=>{string saved=job.Projection.HistoryRunId;history.CopyCompletedRunFrom(job.SourcePath,job.RunId,saved);job.Result=new {saved=true,runId=saved};});return StatusObject(job);
            }
        }
        internal object RemovePosition(string body)
        {
            var request=new JavaScriptSerializer().Deserialize<Request>(body??"{}")??new Request();
            if(string.IsNullOrWhiteSpace(request.position))throw new InvalidDataException("Position을 선택하세요.");
            Guid token;if(!string.IsNullOrEmpty(request.importRequestId)&&!Guid.TryParseExact(request.importRequestId,"N",out token))return new{ok=false,error="유효하지 않은 입력 요청 ID입니다."};
            lock(_sync)
            {
                if(!string.IsNullOrEmpty(request.importRequestId)&&_job?.ImportRequestId==request.importRequestId&&(_job.Id==request.analysisId||_job.Id==request.importRequestId))return StatusObject(_job);
                if(!string.IsNullOrEmpty(request.importRequestId)&&File.Exists(Path.Combine(_root,request.importRequestId,"run.txt")))return StatusObject(Resolve(request.importRequestId));
                var prior=Resolve(request.analysisId);if(prior.Running||!prior.Completed)return new {ok=false,busy=true,error="분석 갱신 중입니다."};
                if(!File.Exists(Path.Combine(_root,prior.Id,"source.txt")))return StartPositionImport(request,true);
                string id=string.IsNullOrEmpty(request.importRequestId)?Guid.NewGuid().ToString("N"):request.importRequestId,directory=Path.Combine(_root,id);Directory.CreateDirectory(directory);
                var job=new Job{Id=id,SourcePath=Path.Combine(directory,"source.sqlite")};
                RecordImportRequest(job,id,request.position);
                prior.Projection.Dispose();prior.Cancellation.Dispose();_job=job;
                StartOperation(job,()=>{
                    using(var store=new SqliteRunStore(job.SourcePath))
                    {
                        var session=store.Start(new SqliteRunStore.RunStoreStart{SourceType="csv-file-stream",Mode="csv-analysis",SourceName="Position selection"});
                        try{store.CopyAnalysisRows(session,prior.SourcePath,prior.RunId,request.position,prior.Projection.PositionDefinitions);store.Complete(session,"completed","");}
                        catch{store.DiscardFailedImport(session);throw;}
                        job.RunId=session.RunId;File.WriteAllText(Path.Combine(directory,"run.txt"),job.RunId);
                        job.Projection=new AnalysisProjection(job.SourcePath,Path.Combine(directory,"projection.sqlite"),job.RunId);
                        job.Projection.CopyConfigurationFrom(prior.Projection);
                        while(job.Projection.Advance(job.Cancellation.Token,10000))Interlocked.Exchange(ref job.Projected,job.Projection.Cursor);
                        CompleteInitialImport(job);
                    }
                },job.ImportRequestId);return StatusObject(job);
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
                return new {ok=true,analysisId=job.Id,page=job.Projection.DateWindow(request.dateStart,request.dateAnchor,job.Cancellation.Token,request.positions,request.date,request.allDates)};
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
        private void Advance(Job job)
        {
            while(job.Projection.Advance(job.Cancellation.Token))Interlocked.Exchange(ref job.Projected,job.Projection.Cursor);
            if(job.ImportRequestId!=null){string revision=job.Projection.HistoryRunId,proof=Path.Combine(_root,job.Id,"import-committed.txt");job.ImportCommitted=revision==job.ImportRequestId||(File.Exists(proof)&&File.ReadAllText(proof)==job.ImportRequestId);if(job.ImportCommitted)job.Imported=job.Projection.ImportedRowCount(job.ImportPosition);}
        }
        private void StartOperation(Job job,Action action,string operationId=null)
        {
            job.Cancellation?.Dispose();job.Cancellation=CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
            job.Running=true;job.Completed=false;job.Error=null;job.Copy=null;
            job.OperationId=operationId??Guid.NewGuid().ToString("N");job.Result=null;
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
            string importReference=Path.Combine(directory,"import-request.json");
            if(File.Exists(importReference))try{var marker=new JavaScriptSerializer().Deserialize<ImportRequestMarker>(File.ReadAllText(importReference));Guid token;if(marker!=null&&Guid.TryParseExact(marker.id,"N",out token)){job.ImportRequestId=marker.id;job.ImportPosition=marker.position;}}catch(ArgumentException){}catch(InvalidOperationException){}
            string sourceReference=Path.Combine(directory,"source.txt");
            if(File.Exists(sourceReference))job.SourcePath=File.ReadAllText(sourceReference).Trim();
            if(_job!=null && _job.Projection!=null) _job.Projection.Dispose();
            if(_job!=null && _job.Cancellation!=null) _job.Cancellation.Dispose();
            // Recovery completes an interrupted projection before exposing final statistics.
            job.Running=true; _job=job;
            job.Task=Task.Run(()=>{
                try { job.Projection=new AnalysisProjection(job.SourcePath,Path.Combine(directory,"projection.sqlite"),runId);Advance(job);job.Completed=true; }
                catch(Exception ex) {job.Error=ex.Message;}
                finally {job.Running=false;}
            });
            return job;
        }
        private static object StatusObject(Job job)
        { return new {ok=string.IsNullOrEmpty(job.Error),analysisId=job.Id,runId=job.RunId,running=job.Running,completed=job.Completed,importRequestId=job.ImportRequestId,importCommitted=job.ImportCommitted,imported=Interlocked.Read(ref job.Imported),projectedCursor=Interlocked.Read(ref job.Projected),copyProcessed=job.Copy==null?0:Interlocked.Read(ref job.Copy.Processed),copyTotal=job.Copy==null?0:Interlocked.Read(ref job.Copy.Total),error=job.Error,operationId=job.OperationId,result=job.Completed?job.Result:null}; }
        public void Dispose()
        {
            _stop.Cancel();
            Job job; lock(_sync) job=_job;
            if(job!=null && job.Task!=null) { try {job.Task.Wait();} catch(AggregateException){} }
            if(job!=null && job.Projection!=null) job.Projection.Dispose();
            if(job!=null && job.Cancellation!=null) job.Cancellation.Dispose();
            foreach(var export in _exports.Values)
            {if(export.Task!=null)try{export.Task.Wait();}catch(AggregateException){}export.Cancellation.Dispose();}
            _stop.Dispose();
        }
    }
}
