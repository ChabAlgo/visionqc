using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using VisionQC.LocalAgent.Domain;
using VisionQC.LocalAgent.Persistence;

namespace VisionQC.LocalAgent.Services
{
    // HTTP 서버는 라우팅만 담당하고, CSV 이력 저장/조회 수명주기는 이 서비스가 관리한다.
    internal sealed class HistoryService
    {
        private readonly object _sync = new object();
        private readonly SqliteRunStore _store;
        private readonly JavaScriptSerializer _json;
        private readonly Dictionary<string, SqliteRunStore.RunStoreSession> _browserImports = new Dictionary<string, SqliteRunStore.RunStoreSession>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, HistoryFileImportJob> _fileImports = new Dictionary<string, HistoryFileImportJob>(StringComparer.OrdinalIgnoreCase);

        internal HistoryService(SqliteRunStore store, JavaScriptSerializer json)
        {
            _store = store;
            _json = json;
        }

        internal object ImportBrowserRows(string body)
        {
            AgentHistoryImportRequest request;
            try { request = _json.Deserialize<AgentHistoryImportRequest>(body ?? "{}") ?? new AgentHistoryImportRequest(); }
            catch (Exception ex) { return new { ok = false, error = "SQLite 이력 요청 JSON 오류: " + ex.Message }; }
            string importId = (request.importId ?? "").Trim();
            if (string.IsNullOrWhiteSpace(importId)) return new { ok = false, error = "SQLite 이력 importId가 필요합니다." };
            if (request.records != null && request.records.Count > 250)
                return new { ok = false, error = "SQLite 이력 저장은 요청당 250행 이하만 허용합니다." };

            try
            {
                lock (_sync)
                {
                    SqliteRunStore.RunStoreSession session;
                    if (request.begin)
                    {
                        if (_browserImports.TryGetValue(importId, out session)) _store.Complete(session, "replaced", "새 CSV 저장 요청으로 교체됨");
                        session = _store.Start(new SqliteRunStore.RunStoreStart
                        {
                            SourceType = "csv-import",
                            Mode = FirstNonEmpty(request.mode, "analysis"),
                            SourceName = request.sourceName ?? "CSV 분석 결과",
                            AgentVersion = Program.AgentVersion,
                            WebVersion = request.webVersion ?? "",
                            NamingProfile = request.namingProfile,
                            NamingProfileJson = _json.Serialize(request.namingProfile ?? new NamingProfile()),
                            WorkspaceType = "csv-analysis",
                            WorkspaceName = request.sourceName ?? "CSV 분석 결과",
                            WorkspaceKey = "csv|" + (request.sourceName ?? "CSV 분석 결과").Trim().ToLowerInvariant()
                        });
                        _browserImports[importId] = session;
                    }
                    else if (!_browserImports.TryGetValue(importId, out session))
                    {
                        return new { ok = false, error = "시작되지 않았거나 만료된 SQLite 이력 저장 요청입니다." };
                    }

                    foreach (AgentHistoryRecordRequest record in request.records ?? new List<AgentHistoryRecordRequest>()) _store.AppendImportedRecord(session, record);
                    if (request.complete)
                    {
                        _store.Complete(session, "completed", "사용자 요청으로 CSV 분석 이력 저장 완료");
                        _browserImports.Remove(importId);
                    }
                    return new { ok = true, importId = importId, saved = session.RecordCount, completed = request.complete, databasePath = _store.DatabasePath };
                }
            }
            catch (Exception ex)
            {
                return new { ok = false, error = "SQLite CSV 이력 저장 실패: " + ex.Message };
            }
        }

        internal AgentHistorySearchResponse Search(string body)
        {
            try
            {
                var request = _json.Deserialize<AgentHistorySearchRequest>(body ?? "{}") ?? new AgentHistorySearchRequest();
                return _store.Search(request);
            }
            catch (Exception ex)
            {
                return new AgentHistorySearchResponse { ok = false, error = "SQLite 이력 조회 실패: " + ex.Message, databasePath = _store.DatabasePath };
            }
        }

        internal AgentHistoryFileImportStatus StartFileImport(string body)
        {
            AgentHistoryFileImportRequest request;
            try { request = _json.Deserialize<AgentHistoryFileImportRequest>(body ?? "{}") ?? new AgentHistoryFileImportRequest(); }
            catch (Exception ex) { return Failure("", "대용량 CSV 설정 JSON 오류: " + ex.Message); }
            string path = (request.filePath ?? "").Trim();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return Failure(path, "CSV 파일을 찾을 수 없습니다.");
            if (!string.Equals(Path.GetExtension(path), ".csv", StringComparison.OrdinalIgnoreCase)) return Failure(path, "대용량 직접 가져오기는 CSV 파일만 지원합니다. XLSX는 기존 분석 파일 불러오기를 사용하세요.");

            lock (_sync)
            {
                foreach (HistoryFileImportJob current in _fileImports.Values)
                    if (current.Running) return ToStatus(current, "다른 대용량 CSV 가져오기가 진행 중입니다.");

                var job = new HistoryFileImportJob
                {
                    JobId = "csv-file-" + Guid.NewGuid().ToString("N"),
                    FilePath = Path.GetFullPath(path),
                    Running = true
                };
                _fileImports[job.JobId] = job;
                Task.Run(() => RunFileImport(job, request));
                return ToStatus(job, null);
            }
        }

        internal AgentHistoryFileImportStatus FileImportStatus(string body)
        {
            AgentHistoryFileImportStatus request;
            try { request = _json.Deserialize<AgentHistoryFileImportStatus>(body ?? "{}") ?? new AgentHistoryFileImportStatus(); }
            catch (Exception ex) { return Failure("", "가져오기 상태 요청 오류: " + ex.Message); }
            lock (_sync)
            {
                HistoryFileImportJob job;
                if (string.IsNullOrWhiteSpace(request.jobId) || !_fileImports.TryGetValue(request.jobId, out job))
                    return Failure("", "대용량 CSV 가져오기 작업을 찾을 수 없습니다.");
                return ToStatus(job, null);
            }
        }

        internal void Dispose()
        {
            lock (_sync)
            {
                foreach (SqliteRunStore.RunStoreSession session in _browserImports.Values)
                    try { _store.Complete(session, "interrupted", "Agent 종료"); } catch { }
                _browserImports.Clear();
            }
        }

        private void RunFileImport(HistoryFileImportJob job, AgentHistoryFileImportRequest request)
        {
            try
            {
                var importer = new CsvHistoryFileImporter(_store, _json);
                importer.Import(request, job.FilePath, processed => Interlocked.Exchange(ref job.Processed, processed));
                job.Completed = true;
            }
            catch (Exception ex)
            {
                job.Error = "대용량 CSV 가져오기 실패: " + ex.Message;
            }
            finally
            {
                job.Running = false;
            }
        }

        private AgentHistoryFileImportStatus ToStatus(HistoryFileImportJob job, string error)
        {
            return new AgentHistoryFileImportStatus
            {
                ok = string.IsNullOrWhiteSpace(error) && string.IsNullOrWhiteSpace(job.Error),
                running = job.Running,
                completed = job.Completed,
                jobId = job.JobId,
                filePath = job.FilePath,
                processed = Interlocked.Read(ref job.Processed),
                error = FirstNonEmpty(error, job.Error),
                databasePath = _store.DatabasePath
            };
        }

        private AgentHistoryFileImportStatus Failure(string path, string error)
        {
            return new AgentHistoryFileImportStatus { ok = false, filePath = path ?? "", error = error, databasePath = _store.DatabasePath };
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (string value in values) if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
            return "";
        }

        private sealed class HistoryFileImportJob
        {
            internal string JobId;
            internal string FilePath;
            internal long Processed;
            internal volatile bool Running;
            internal volatile bool Completed;
            internal string Error;
        }
    }
}
