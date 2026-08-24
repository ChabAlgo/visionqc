using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace VisionQC.LocalAgent.Services
{
    // Windows Explorer 선택 창의 수명주기와 브라우저별 소유권을 AgentServer에서 분리한다.
    // HTTP 계층은 JSON을 읽어 이 서비스에 전달만 하므로 선택기 수정이 VPDL 실행 경로에 영향을 주지 않는다.
    internal sealed class PickerService : IDisposable
    {
        private readonly object _exclusiveSync = new object();
        private readonly object _stateSync = new object();
        private readonly Action<string, string> _log;
        private string _legacyClientId = "";
        private PickerJob _job;

        public PickerService(Action<string, string> log)
        {
            _log = log ?? ((level, message) => { });
        }

        public object Start(Dictionary<string, object> data)
        {
            string clientId = GetString(data, "clientId", "");
            string requestId = GetString(data, "requestId", "");
            string kind = GetString(data, "kind", "folder").Trim().ToLowerInvariant();
            string initial = GetString(data, "initialPath", "");
            string fileType = GetString(data, "fileType", kind == "file" ? "workspace" : "folder");
            bool allowMultiple = kind == "folder" && GetBool(data, "multiple", false);
            if (string.IsNullOrWhiteSpace(clientId))
                return new { ok = false, pending = false, error = "브라우저 선택 세션 ID가 없습니다." };
            if (string.IsNullOrWhiteSpace(requestId)) requestId = Guid.NewGuid().ToString("N");
            if (kind != "file" && kind != "folder")
                return new { ok = false, pending = false, error = "지원하지 않는 선택 종류입니다." };

            PickerJob job;
            lock (_stateSync)
            {
                if (_job != null)
                {
                    bool sameRequest = string.Equals(_job.ClientId, clientId, StringComparison.Ordinal) &&
                        string.Equals(_job.RequestId, requestId, StringComparison.Ordinal);
                    bool sameClient = string.Equals(_job.ClientId, clientId, StringComparison.Ordinal);
                    if (sameRequest) return JobResponse(_job);
                    bool expiredCompleted = _job.Completed && _job.CompletedUtc != DateTime.MinValue &&
                        DateTime.UtcNow - _job.CompletedUtc > TimeSpan.FromSeconds(30);
                    bool replaceable = _job.Completed && (_job.Delivered || sameClient || expiredCompleted);
                    if (!replaceable)
                    {
                        bool recoverable = sameClient;
                        return new
                        {
                            ok = false,
                            pending = false,
                            busy = true,
                            recoverable = recoverable,
                            requestId = _job.RequestId,
                            error = recoverable
                                ? "이 브라우저의 이전 파일/폴더 선택 작업이 남아 있습니다. Agent가 기존 작업을 정리한 뒤 다시 시도합니다."
                                : "다른 브라우저에서 파일/폴더 선택 창을 사용 중입니다."
                        };
                    }
                }

                job = new PickerJob
                {
                    ClientId = clientId,
                    RequestId = requestId,
                    Kind = kind,
                    FileType = fileType,
                    InitialPath = initial,
                    AllowMultiple = allowMultiple
                };
                _job = job;
            }

            // HTTP 응답을 먼저 반환하고 Explorer는 별도 STA 작업에서 표시한다.
            NativeShellPicker.PrepareDialogRequest();
            _ = Task.Run(() => RunJob(job));
            return JobResponse(job);
        }

        public object Status(Dictionary<string, object> data)
        {
            string clientId = GetString(data, "clientId", "");
            string requestId = GetString(data, "requestId", "");
            lock (_stateSync)
            {
                if (_job == null)
                    return new { ok = false, pending = false, requestId = requestId, error = "Agent에 해당 선택 작업이 없습니다. 다시 선택하세요." };
                if (!string.Equals(_job.ClientId, clientId, StringComparison.Ordinal) ||
                    !string.Equals(_job.RequestId, requestId, StringComparison.Ordinal))
                    return new { ok = false, pending = false, requestId = requestId, error = "다른 브라우저 선택 작업의 결과는 조회할 수 없습니다." };

                object response = JobResponse(_job);
                if (_job.Completed) _job.Delivered = true;
                return response;
            }
        }

        public object PickFolder(Dictionary<string, object> data)
        {
            string initial = GetString(data, "initialPath", "");
            string clientId = GetString(data, "clientId", "");
            bool recoverable;
            if (!TryBeginLegacyPicker(clientId, out recoverable)) return BusyResponse(recoverable);
            try
            {
                _log("INFO", "폴더 선택 창 열림");
                NativeShellPicker.PrepareDialogRequest();
                string selected = NativeShellPicker.PickFolder(initial);
                _log("INFO", string.IsNullOrWhiteSpace(selected) ? "폴더 선택 취소" : "폴더 선택 완료: " + selected);
                return new { ok = !string.IsNullOrWhiteSpace(selected), path = selected ?? "" };
            }
            catch (Exception ex)
            {
                _log("ERROR", "폴더 선택 실패: " + ex.Message);
                return new { ok = false, path = "", error = ex.Message };
            }
            finally { EndLegacyPicker(clientId); }
        }

        public object PickFile(Dictionary<string, object> data)
        {
            string initial = GetString(data, "initialPath", "");
            string fileType = GetString(data, "fileType", "workspace");
            string clientId = GetString(data, "clientId", "");
            bool recoverable;
            if (!TryBeginLegacyPicker(clientId, out recoverable)) return BusyResponse(recoverable);
            try
            {
                _log("INFO", "파일 선택 창 열림: " + fileType);
                NativeShellPicker.PrepareDialogRequest();
                string selected = NativeShellPicker.PickFile(initial, fileType);
                _log("INFO", string.IsNullOrWhiteSpace(selected) ? "파일 선택 취소" : "파일 선택 완료: " + selected);
                return new { ok = !string.IsNullOrWhiteSpace(selected), path = selected ?? "" };
            }
            catch (Exception ex)
            {
                _log("ERROR", "파일 선택 실패: " + ex.Message);
                return new { ok = false, path = "", error = ex.Message };
            }
            finally { EndLegacyPicker(clientId); }
        }

        public object Cancel(Dictionary<string, object> data)
        {
            string clientId = GetString(data, "clientId", "");
            string requestId = GetString(data, "requestId", "");
            bool asyncPicker = false;
            string activeClientId;
            lock (_stateSync)
            {
                if (_job != null && !_job.Completed)
                {
                    asyncPicker = true;
                    activeClientId = _job.ClientId;
                    if (!string.Equals(activeClientId, clientId, StringComparison.Ordinal) ||
                        (!string.IsNullOrWhiteSpace(requestId) && !string.Equals(_job.RequestId, requestId, StringComparison.Ordinal)))
                        return new { ok = false, cancelled = false, error = "다른 브라우저 세션의 선택 창은 취소할 수 없습니다." };
                    _job.CancelRequested = true;
                    // Explorer가 늦게 종료되어도, 이전 작업이 이 취소 결과를 덮어쓰지 못하게 한다.
                    _job.Path = "";
                    _job.Paths = new List<string>();
                    _job.Error = "";
                    _job.Cancelled = true;
                    _job.Completed = true;
                    _job.CompletedUtc = DateTime.UtcNow;
                }
                else activeClientId = _legacyClientId;
            }

            if (string.IsNullOrWhiteSpace(activeClientId))
                return new { ok = true, cancelled = false, message = "열린 선택 창이 없습니다." };
            if (!asyncPicker && (string.IsNullOrWhiteSpace(clientId) || !string.Equals(activeClientId, clientId, StringComparison.Ordinal)))
                return new { ok = false, cancelled = false, error = "다른 브라우저 세션의 선택 창은 취소할 수 없습니다." };

            bool cancelled = NativeShellPicker.CancelActiveDialog();
            _log("INFO", cancelled ? "이전 파일/폴더 선택 작업을 정리했습니다." : "파일/폴더 선택 창 닫기 요청 시 활성 Shell Dialog가 없었습니다.");
            return new { ok = true, cancelled = cancelled };
        }

        public void Dispose()
        {
            try { NativeShellPicker.CancelActiveDialog(); } catch { }
        }

        private void RunJob(PickerJob job)
        {
            var selectedPaths = new List<string>();
            string error = "";
            try
            {
                lock (_stateSync)
                {
                    if (job.CancelRequested)
                    {
                        job.Completed = true;
                        job.Cancelled = true;
                        job.CompletedUtc = DateTime.UtcNow;
                        return;
                    }
                }

                _log("INFO", job.Kind == "file" ? "파일 선택 창 열림: " + job.FileType : (job.AllowMultiple ? "다중 폴더 선택 창 열림" : "폴더 선택 창 열림"));
                if (job.Kind == "file")
                {
                    string selected = NativeShellPicker.PickFile(job.InitialPath, job.FileType);
                    if (!string.IsNullOrWhiteSpace(selected)) selectedPaths.Add(selected);
                }
                else selectedPaths.AddRange(NativeShellPicker.PickFolders(job.InitialPath, job.AllowMultiple));

                _log("INFO", selectedPaths.Count == 0
                    ? (job.Kind == "file" ? "파일 선택 취소" : "폴더 선택 취소")
                    : (job.Kind == "file" ? "파일 선택 완료: " : "폴더 선택 완료: ") + string.Join(" | ", selectedPaths));
            }
            catch (Exception ex)
            {
                error = ex.Message;
                _log("ERROR", (job.Kind == "file" ? "파일" : "폴더") + " 선택 실패: " + ex.Message);
            }
            finally
            {
                lock (_stateSync)
                {
                    if (!(job.Completed && job.CancelRequested))
                    {
                        job.Paths = selectedPaths;
                        job.Path = selectedPaths.Count == 0 ? "" : selectedPaths[0];
                        job.Error = error;
                        job.Cancelled = selectedPaths.Count == 0 && string.IsNullOrWhiteSpace(error);
                        job.Completed = true;
                        job.CompletedUtc = DateTime.UtcNow;
                    }
                }
            }
        }

        private bool TryBeginLegacyPicker(string clientId, out bool recoverable)
        {
            if (!Monitor.TryEnter(_exclusiveSync))
            {
                lock (_stateSync)
                    recoverable = !string.IsNullOrWhiteSpace(clientId) && string.Equals(_legacyClientId, clientId, StringComparison.Ordinal);
                return false;
            }
            lock (_stateSync) _legacyClientId = clientId ?? "";
            recoverable = false;
            return true;
        }

        private void EndLegacyPicker(string clientId)
        {
            lock (_stateSync)
            {
                if (string.Equals(_legacyClientId, clientId ?? "", StringComparison.Ordinal)) _legacyClientId = "";
            }
            Monitor.Exit(_exclusiveSync);
        }

        private static object JobResponse(PickerJob job)
        {
            if (!job.Completed) return new { ok = true, pending = true, requestId = job.RequestId, started = true };
            return new
            {
                ok = string.IsNullOrWhiteSpace(job.Error) && !job.Cancelled && !string.IsNullOrWhiteSpace(job.Path),
                pending = false,
                requestId = job.RequestId,
                path = job.Path ?? "",
                paths = job.Paths ?? new List<string>(),
                cancelled = job.Cancelled,
                error = job.Error ?? ""
            };
        }

        private static object BusyResponse(bool recoverable)
        {
            return new { ok = false, busy = true, recoverable = recoverable, path = "", error = "다른 파일/폴더 선택 창이 이미 열려 있습니다. 열린 창을 완료하거나 취소하세요." };
        }

        private static string GetString(Dictionary<string, object> data, string key, string fallback)
        {
            object value;
            return data != null && data.TryGetValue(key, out value) && value != null ? Convert.ToString(value) : fallback;
        }

        private static bool GetBool(Dictionary<string, object> data, string key, bool fallback)
        {
            object value;
            if (data == null || !data.TryGetValue(key, out value) || value == null) return fallback;
            bool result;
            return bool.TryParse(Convert.ToString(value), out result) ? result : fallback;
        }

        private sealed class PickerJob
        {
            public string ClientId = "";
            public string RequestId = "";
            public string Kind = "folder";
            public string FileType = "folder";
            public string InitialPath = "";
            public string Path = "";
            public List<string> Paths = new List<string>();
            public string Error = "";
            public bool AllowMultiple;
            public bool Completed;
            public bool Cancelled;
            public bool CancelRequested;
            public bool Delivered;
            public DateTime CompletedUtc;
        }
    }
}
