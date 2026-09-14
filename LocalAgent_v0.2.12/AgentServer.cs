using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using VisionQC.LocalAgent.Domain;
using VisionQC.LocalAgent.Persistence;
using VisionQC.LocalAgent.Services;
using VpdlGreenHeatmapOverlay;
using SysException = System.Exception;
using SysInvalidOperationException = System.InvalidOperationException;
using VpdlGpuMode = ViDi2.GpuMode;
using LocalRuntime = ViDi2.Runtime.Local;

namespace VisionQC.LocalAgent
{
    internal sealed class AgentServer : IDisposable
    {
        private readonly int _port;
        private readonly int _parentProcessId;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        private readonly object _sync = new object();
        private readonly object _vpdlSync = new object();
        // Simulation/단일 검사 이력 기록은 하나의 SQLite writer 순서를 유지한다.
        private readonly object _historyWriteSync = new object();
        private readonly List<SseClient> _sse = new List<SseClient>();
        private readonly Dictionary<string, WorkspaceInspectionCacheEntry> _workspaceInspectionCache = new Dictionary<string, WorkspaceInspectionCacheEntry>(StringComparer.OrdinalIgnoreCase);
        private TcpListener _listener;
        private CancellationTokenSource _serverCts = new CancellationTokenSource();
        private CancellationTokenSource _simulationCts;
        private Task _simulationTask;
        private readonly string _instanceId = Guid.NewGuid().ToString("N");
        private string _licenseStatus = "검사 엔진 로드 대기";
        private string _runtimeMessage = "Agent 준비 완료 · GPU/License는 Runtime File Load에서 초기화합니다.";
        private readonly string _vpdlVersion;
        private readonly string _gpuName;
        private readonly List<int> _gpuDeviceIndices;
        private DateTime _lastProgressBroadcast = DateTime.MinValue;
        private DateTime _simulationStartedUtc = DateTime.MinValue;
        private int _liveRecordCount = 0;
        private int _lastProgressValue = -1;
        private SimulationState _state = NewIdleState();
        private readonly List<LiveAnalysisRecord> _liveBuffer = new List<LiveAnalysisRecord>();
        private int _liveBatchSize = 100;
        private LocalRuntime.Control _inspectionControl;
        private string _inspectionControlKey = "";
        private bool _inspectionControlDeferred;
        private bool _vpdlReservedForSimulation;
        private LocalRuntime.Control _preloadedRuntimeControl;
        private readonly Dictionary<string, PositionWorkerClient> _preloadedPositionWorkers = new Dictionary<string, PositionWorkerClient>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _preloadedPositionGpuAssignments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly object _parallelProgressSync = new object();
        private Dictionary<string, PositionProgressState> _parallelProgressStates;
        private Dictionary<string, string> _parallelPositionDisplayNames;
        private string _parallelCoordinatorToken = "";
        private string _forwardCoordinatorUrl = "";
        private string _forwardCoordinatorToken = "";
        private string _forwardPositionKey = "";
        private string _preloadedRuntimeSignature = "";
        private string _preloadedRuntimeToken = "";
        private string _preloadedRuntimeMode = "";
        // Runtime Control 생성 조건과 Green Workspace 집합을 별도로 보관한다.
        // Integrated Runtime은 같은 GPU 조건·Green Workspace 집합의 Green 단독 실행에서 재사용할 수 있다.
        private string _preloadedRuntimeControlSignature = "";
        private string _preloadedGreenWorkspaceSignature = "";
        private string _lastAgentLogKey = "";
        private DateTime _lastAgentLogUtc = DateTime.MinValue;
        private readonly PickerService _picker;
        private readonly ImagePreviewService _imagePreview;
        private readonly SqliteRunStore _historyStore;
        private readonly HistoryService _history;
        private SqliteRunStore.RunStoreSession _simulationHistorySession;
        private bool _simulationHistoryWriteFailed;
        private string _lastSimulationRunId = "";

        public AgentServer()
        {
            int configuredPort;
            _port = int.TryParse(Environment.GetEnvironmentVariable("VISIONQC_AGENT_PORT"), out configuredPort) && configuredPort > 0 ? configuredPort : 17891;
            int configuredParentProcessId;
            _parentProcessId = int.TryParse(Environment.GetEnvironmentVariable("VISIONQC_AGENT_PARENT_PID"), out configuredParentProcessId) && configuredParentProcessId > 0 ? configuredParentProcessId : 0;
            _vpdlVersion = DetectVpdlVersion();
            _gpuName = DetectGpuName();
            _gpuDeviceIndices = DetectGpuDeviceIndices();
            AgentDiagnostics.Write("GPU", _gpuName);
            AgentDiagnostics.Write("GPU_DEVICES", _gpuDeviceIndices.Count == 0 ? "-" : string.Join(",", _gpuDeviceIndices));
            Task.Run(() => AgentDiagnostics.WriteNvidiaEnvironment());
            _picker = new PickerService(AppendAgentLog);
            _imagePreview = new ImagePreviewService();
            _historyStore = new SqliteRunStore(ResolveHistoryDatabasePath());
            _history = new HistoryService(_historyStore, _json);
            if (_parentProcessId > 0) Task.Run(MonitorParentProcess);
        }

        private void MonitorParentProcess()
        {
            while (!_serverCts.IsCancellationRequested)
            {
                try
                {
                    using (Process parent = Process.GetProcessById(_parentProcessId))
                        if (parent.HasExited) { _serverCts.Cancel(); return; }
                }
                catch { _serverCts.Cancel(); return; }
                Thread.Sleep(1000);
            }
        }

        public void RunUntilExit(bool openOfflinePage = false)
        {
            _listener = new TcpListener(IPAddress.Loopback, _port);
            try
            {
                _listener.Start();
            }
            catch (SocketException ex)
            {
                MessageBox.Show("VisionQC Local Agent가 이미 실행 중이거나 포트 17891을 사용할 수 없습니다.\r\n\r\n" + ex.Message,
                    "VisionQC Local Agent", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Task.Run(() => AcceptLoop(_serverCts.Token));
            // Do not create/dispose a GPU Control before the user's actual runtime.
            AgentDiagnostics.Write("GPU_LIFECYCLE", "Startup probe skipped; GPU initialization is reserved for Runtime File Load");
            if (openOfflinePage) Task.Run(OpenOfflinePage);

            while (!_serverCts.IsCancellationRequested) Thread.Sleep(250);
        }

        // 테스트/복구 도구에서만 VISIONQC_HISTORY_DB_PATH로 별도 SQLite 파일을 지정할 수 있다.
        // 일반 설치 실행은 항상 LocalAppData의 영구 이력 DB를 사용한다.
        private static string ResolveHistoryDatabasePath()
        {
            string overridePath = Environment.GetEnvironmentVariable("VISIONQC_HISTORY_DB_PATH");
            if (!string.IsNullOrWhiteSpace(overridePath)) return Path.GetFullPath(overridePath.Trim());
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VisionQC", "LocalAgent", "data", "visionqc-history.sqlite");
        }

        private async Task AcceptLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                TcpClient client = null;
                try
                {
                    client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
                    _ = Task.Run(() => HandleClient(client), token);
                }
                catch when (token.IsCancellationRequested) { break; }
                catch { try { client?.Close(); } catch { } }
            }
        }

        private async Task HandleClient(TcpClient client)
        {
            NetworkStream stream = null;
            string origin = "";
            try
            {
                stream = client.GetStream();
                var request = await ReadRequest(stream).ConfigureAwait(false);
                if (request == null) { client.Close(); return; }

                origin = request.Headers.ContainsKey("origin") ? request.Headers["origin"] : "";
                if (request.Method == "OPTIONS")
                {
                    await WriteResponse(stream, 204, "text/plain", "", origin).ConfigureAwait(false);
                    client.Close();
                    return;
                }

                if (request.Method == "GET" && !request.Path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
                {
                    await WriteOfflineWebAsset(stream, request.Path, origin).ConfigureAwait(false);
                    client.Close();
                    return;
                }

                if (request.Path == "/api/events" && request.Method == "GET")
                {
                    await WriteSseHeaders(stream, origin).ConfigureAwait(false);
                    var sse = new SseClient(client, stream);
                    lock (_sync) _sse.Add(sse);
                    await WriteSse(sse, "status", Snapshot()).ConfigureAwait(false);
                    return;
                }

                object result;
                int status = 200;
                switch (request.Path)
                {
                    case "/api/status":
                        result = BuildStatus();
                        break;
                    case "/api/vpdl/versions":
                        result = BuildVpdlVersions();
                        break;
                    case "/api/vpdl/select":
                        result = SelectVpdlWorker(request.Body);
                        break;
                    case "/api/runtime/check":
                        result = RuntimeCheck(request.Body);
                        break;
                    case "/api/runtime/preload":
                        result = PreloadRuntime(request.Body);
                        break;
                    case "/api/parallel/progress":
                        result = ReceiveParallelWorkerProgress(request.Body);
                        break;
                    case "/api/parallel/configure":
                        result = ConfigureParallelWorkerForwarding(request.Body);
                        break;
                    case "/api/workspace/inspect":
                        result = InspectWorkspace(request.Body);
                        break;
                    case "/api/pick/start":
                        result = StartPicker(request.Body);
                        break;
                    case "/api/pick/status":
                        result = PickerStatus(request.Body);
                        break;
                    case "/api/pick/folder":
                        result = PickFolder(request.Body);
                        break;
                    case "/api/pick/file":
                        result = PickFile(request.Body);
                        break;
                    case "/api/pick/cancel":
                        result = CancelPicker(request.Body);
                        break;
                    case "/api/blue/fallback/preview":
                        result = PreviewBlueFallback(request.Body);
                        break;
                    case "/api/naming/preview":
                        result = PreviewNamingProfile(request.Body);
                        break;
                    case "/api/image/preview":
                        result = PreviewImage(request.Body);
                        break;
                    case "/api/history/import":
                        result = ImportHistory(request.Body);
                        break;
                    case "/api/history/search":
                        result = _history.Search(request.Body);
                        break;
                    case "/api/history/import-file/start":
                        result = _history.StartFileImport(request.Body);
                        break;
                    case "/api/history/import-file/status":
                        result = _history.FileImportStatus(request.Body);
                        break;
                    case "/api/history/delete":
                        result = DeleteHistory(request.Body);
                        break;
                    case "/api/classification/inspect":
                        result = InspectSingleGreenImage(request.Body);
                        break;
                    case "/api/classification/inspect-upload":
                        result = InspectUploadedGreenImage(request.Body);
                        break;
                    case "/api/simulation/start":
                        result = StartSimulation(request.Body);
                        break;
                    case "/api/simulation/stop":
                        result = StopSimulation();
                        break;
                    case "/api/simulation/state":
                        result = Snapshot();
                        break;
                    case "/api/simulation/results":
                        result = ReadSimulationResults(request.Body);
                        break;
                    case "/api/agent/unregister":
                        Program.UnregisterProtocol();
                        result = new { ok = true, unregistered = true };
                        _ = Task.Run(async () => { await Task.Delay(250); _serverCts.Cancel(); });
                        break;
                    case "/api/agent/exit":
                        result = new { ok = true };
                        _ = Task.Run(async () => { await Task.Delay(250); _serverCts.Cancel(); });
                        break;
                    default:
                        status = 404;
                        result = new { ok = false, error = "Unknown endpoint" };
                        break;
                }
                await WriteJson(stream, status, result, origin).ConfigureAwait(false);
                client.Close();
            }
            catch (SysException ex)
            {
                // Browser timeout/refresh can close the loopback socket before the response is written.
                // Expected client disconnects are not Agent failures and should not become repeated alarms.
                if (!IsExpectedClientDisconnect(ex))
                    AppendAgentLog("ERROR", "HTTP 요청 처리 실패: " + ex.Message);
                try
                {
                    if (stream != null && stream.CanWrite)
                        await WriteJson(stream, 500, new { ok = false, error = "Agent 내부 요청 처리 실패: " + ex.Message }, origin).ConfigureAwait(false);
                }
                catch { }
                try { client.Close(); } catch { }
            }
        }

        private NamingPreviewResponse PreviewNamingProfile(string body)
        {
            try
            {
                var request = _json.Deserialize<NamingPreviewRequest>(body ?? "{}") ?? new NamingPreviewRequest();
                // 브라우저 실수로 Agent 메모리를 점유하지 않도록 미리보기는 최대 200개 파일로 제한한다.
                if (request.fileNames != null && request.fileNames.Count > 200)
                    request.fileNames = request.fileNames.Take(200).ToList();
                return NamingProfileParser.Preview(request);
            }
            catch (SysException ex)
            {
                return new NamingPreviewResponse { ok = false, error = "파일명 규칙 미리보기 실패: " + ex.Message };
            }
        }

        private AgentImagePreviewResponse PreviewImage(string body)
        {
            try
            {
                return _imagePreview.Create(_json.Deserialize<AgentImagePreviewRequest>(body ?? "{}") ?? new AgentImagePreviewRequest());
            }
            catch (SysException ex)
            {
                return new AgentImagePreviewResponse { ok = false, error = "이미지 미리보기 요청 오류: " + ex.Message };
            }
        }

        private object ImportHistory(string body)
        {
            return _history.ImportBrowserRows(body);
        }

        private object DeleteHistory(string body)
        {
            lock (_sync)
                if (_simulationTask != null && !_simulationTask.IsCompleted)
                    return new AgentHistoryDeleteResponse { ok = false, busy = true, error = "Simulation 실행 중에는 검사 이력을 삭제할 수 없습니다.", databasePath = _historyStore.DatabasePath };
            lock (_historyWriteSync)
                if (_simulationHistorySession != null)
                    return new AgentHistoryDeleteResponse { ok = false, busy = true, error = "Simulation 이력 저장이 끝난 뒤 다시 삭제하세요.", databasePath = _historyStore.DatabasePath };
            return _history.DeleteAll(body);
        }

        private object InspectSingleGreenImage(string body)
        {
            AgentSingleInspectionRequest req;
            try { req = _json.Deserialize<AgentSingleInspectionRequest>(body ?? "{}"); }
            catch (SysException ex) { return new { ok = false, error = "단일 검사 설정 JSON 오류: " + ex.Message }; }

            if (req == null || string.IsNullOrWhiteSpace(req.imagePath) || !File.Exists(req.imagePath))
                return new { ok = false, error = "단일 검사할 이미지 파일을 찾을 수 없습니다." };
            var resolution = PositionResolver.Resolve(req.imagePath, req.positions);
            if (resolution.matches.Count == 0)
                return new { ok = false, error = "파일명에서 활성 Position을 찾지 못했습니다. 파일명에 Position 문자열이 있어야 합니다." };
            if (!resolution.IsUnique)
                return new { ok = false, error = "파일명에 여러 Position이 동시에 일치합니다: " + string.Join(", ", resolution.matches.Select(x => x.displayName)) };

            AgentPositionRequest position = resolution.Position;
            if (string.IsNullOrWhiteSpace(FirstNonEmpty(position.greenWorkspacePath, position.workspacePath)))
                return new { ok = false, error = position.displayName + " Green Workspace가 설정되지 않았습니다." };

            LocalRuntime.Control control = null;
            PositionWorkerClient positionWorker = null;
            string signature = BuildRuntimePreloadSignature(req);
            lock (_vpdlSync) {
                if (_vpdlReservedForSimulation)
                    return new { ok = false, busy = true, error = "다른 VPDL 작업이 실행 중입니다. 완료 후 AI Suggest를 실행하세요." };
                if (!HasCompatiblePreloadedRuntime(req, signature))
                    return new { ok = false, error = "현재 설정과 일치하는 사전 로드 Runtime이 없습니다. Simulation에서 Runtime File Load를 먼저 실행하세요." };
                if (_preloadedPositionWorkers.Count > 0)
                {
                    if (!_preloadedPositionWorkers.TryGetValue(position.key, out positionWorker))
                        return new { ok = false, error = position.displayName + " Position Worker가 없습니다. Runtime File Load를 다시 실행하세요." };
                    _vpdlReservedForSimulation = true;
                }
            }
            if (positionWorker != null)
            {
                try
                {
                    AgentSingleInspectionRequest childRequest = _json.Deserialize<AgentSingleInspectionRequest>(_json.Serialize(
                        ClonePositionRequest(req, position, _preloadedPositionGpuAssignments)));
                    childRequest.imagePath = req.imagePath;
                    Dictionary<string, object> result = WorkerPost<Dictionary<string, object>>(positionWorker, "/api/classification/inspect", childRequest, 4 * 60 * 1000);
                    AppendAgentLog("INFO", "Position Worker 단일 Green 검사 완료 | " + position.displayName + " | " + Path.GetFileName(req.imagePath));
                    return result;
                }
                catch (SysException ex) { return new { ok = false, error = "Position Worker 단일 Green 검사 실패: " + ex.Message }; }
                finally { lock (_vpdlSync) _vpdlReservedForSimulation = false; }
            }
            lock (_vpdlSync)
            {
                if (_vpdlReservedForSimulation)
                    return new { ok = false, busy = true, error = "다른 VPDL 작업이 실행 중입니다. 단일 검사는 작업 완료 후 다시 시도하세요." };
                if (!HasCompatiblePreloadedRuntime(req, signature))
                    return new { ok = false, error = "현재 설정에 맞는 Runtime 사전 로드가 없습니다. Green 모드의 Runtime File Load를 먼저 실행하세요." };
                _vpdlReservedForSimulation = true;
                control = _preloadedRuntimeControl;
                _preloadedRuntimeControl = null;
                _preloadedRuntimeSignature = "";
                _preloadedRuntimeToken = "";
                _preloadedRuntimeMode = "";
                _preloadedRuntimeControlSignature = "";
                _preloadedGreenWorkspaceSignature = "";
                DisposeInspectionControlLocked();
            }

            bool reusable = true;
            try
            {
                // 단일 검사는 현재 사전 로드된 Runtime만 재사용한다. 분석 이력 SQLite에는 저장하지 않는다.
                var config = BuildGreenConfig(req, SingleInspectionOutputRoot(), null, false);
                config.HeatmapImageSave = req.green != null && req.green.heatmapImageSave;
                config.WorkspaceSlots = config.WorkspaceSlots.Where(x => string.Equals(x.Key, position.key, StringComparison.OrdinalIgnoreCase)).ToList();
                foreach (var tool in config.Tools) tool.PositionKeys = new List<string> { position.key };
                LiveAnalysisRecord record = GreenOverlayProcessor.InspectSingle(config, position.key, req.imagePath, control, true, CancellationToken.None);
                AppendAgentLog("INFO", "단일 Green 검사 완료 | " + position.displayName + " | " + Path.GetFileName(req.imagePath) + " | " + record.TotalResult);
                return new { ok = true, position = position.displayName, key = position.key, record = record };
            }
            catch (SysException ex)
            {
                reusable = false;
                AppendAgentLog("ERROR", "단일 Green 검사 실패: " + ex.Message);
                return new { ok = false, error = "단일 Green 검사 실패: " + ex.Message };
            }
            finally
            {
                lock (_vpdlSync)
                {
                    if (reusable && control != null)
                    {
                        DisposePreloadedRuntimeLocked();
                        RememberPreloadedRuntimeLocked(control, req, signature);
                        _licenseStatus = "Runtime Ready";
                        _runtimeMessage = "단일 Green 검사 완료 · 사전 로드 Runtime 재사용 가능";
                        control = null;
                    }
                    if (control != null)
                    {
                        try { RuntimeWorkspaceRegistry.Remove(control); control.Dispose(); } catch { }
                    }
                    _vpdlReservedForSimulation = false;
                }
            }
        }

        // Gemini 같은 외부 AI 호출 없이, 브라우저에서 선택한 이미지 1장을 현재 Runtime으로 검사한다.
        // 브라우저는 로컬 절대 경로를 노출하지 않으므로 이 경로에서만 짧게 임시 파일을 사용한다.
        private object InspectUploadedGreenImage(string body)
        {
            AgentUploadedInspectionRequest req;
            try { req = _json.Deserialize<AgentUploadedInspectionRequest>(body ?? "{}"); }
            catch (SysException ex) { return new { ok = false, error = "분류 AI Suggest 요청 JSON 오류: " + ex.Message }; }
            if (req == null || string.IsNullOrWhiteSpace(req.imageBase64)) return new { ok = false, error = "검사할 이미지 데이터가 없습니다." };
            if (req.imageBase64.Length > 110 * 1024 * 1024) return new { ok = false, error = "AI Suggest 이미지는 80MB 이하만 지원합니다." };

            byte[] bytes;
            try { bytes = Convert.FromBase64String(req.imageBase64); }
            catch (SysException ex) { return new { ok = false, error = "이미지 base64 형식 오류: " + ex.Message }; }
            if (bytes.Length == 0 || bytes.Length > 80 * 1024 * 1024) return new { ok = false, error = "AI Suggest 이미지는 80MB 이하만 지원합니다." };

            string originalName = Path.GetFileName(req.fileName ?? "image.png");
            if (string.IsNullOrWhiteSpace(originalName)) originalName = "image.png";
            foreach (char invalid in Path.GetInvalidFileNameChars()) originalName = originalName.Replace(invalid, '_');
            string extension = Path.GetExtension(originalName);
            if (string.IsNullOrWhiteSpace(extension)) originalName += MimeImageExtension(req.mimeType);
            string temporaryRoot = Path.Combine(Path.GetTempPath(), "VisionQC", "classification-inspection");
            string temporaryPath = Path.Combine(temporaryRoot, Guid.NewGuid().ToString("N") + "_" + originalName);
            try
            {
                Directory.CreateDirectory(temporaryRoot);
                File.WriteAllBytes(temporaryPath, bytes);
                req.imagePath = temporaryPath;
                return InspectSingleGreenImage(_json.Serialize(req));
            }
            catch (SysException ex)
            {
                return new { ok = false, error = "AI Suggest 임시 이미지 검사 실패: " + ex.Message };
            }
            finally
            {
                try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); } catch { }
            }
        }

        private static string MimeImageExtension(string mimeType)
        {
            string type = (mimeType ?? "").Trim().ToLowerInvariant();
            if (type == "image/jpeg") return ".jpg";
            if (type == "image/bmp") return ".bmp";
            if (type == "image/tiff") return ".tif";
            if (type == "image/webp") return ".webp";
            return ".png";
        }

        private static string SingleInspectionOutputRoot()
        {
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VisionQC", "LocalAgent", "output", "single-inspection");
            Directory.CreateDirectory(root);
            return root;
        }

        private object BuildStatus()
        {
            var state = Snapshot();
            return new
            {
                ok = true,
                vpdlAvailable = true,
                instanceId = _instanceId,
                agentVersion = Program.AgentVersion,
                engineVersion = "DL_Simulation v1.13 + VisionQC Workspace Inspect",
                installedVpdlVersion = _vpdlVersion,
                activeVpdlApiVersion = Program.ActiveVpdlInstallation == null ? "-" : Program.ActiveVpdlInstallation.ApiVersion,
                vpdlWorkerMode = Environment.GetEnvironmentVariable("VISIONQC_VPDL_WORKER_MODE") ?? "exact",
                availableVpdlVersions = VpdlRuntimeCatalog.Discover().Select(item => new { productVersion = item.ProductVersion, apiVersion = item.ApiVersion, displayName = item.DisplayName, workerInstalled = VpdlWorkerLocator.IsAvailable(Program.AgentHomeDirectory, item.ApiVersion) }).ToArray(),
                vpdlVersion = (HasAnyPreloadedRuntimeLocked() || _vpdlReservedForSimulation) ? _vpdlVersion : "-",
                license = _licenseStatus,
                runtimeMessage = _runtimeMessage,
                gpu = _gpuName,
                gpuDeviceIndices = _gpuDeviceIndices.ToArray(),
                running = state.running,
                runtimePreloaded = HasAnyPreloadedRuntimeLocked(),
                runtimePreloadMode = _preloadedRuntimeMode,
                runtimePreloadToken = _preloadedRuntimeToken,
                runtimePreloadSignature = _preloadedRuntimeSignature,
                runtimePreloadControlSignature = _preloadedRuntimeControlSignature,
                runtimePreloadGreenWorkspaceSignature = _preloadedGreenWorkspaceSignature,
                positionGpuAssignments = _preloadedPositionGpuAssignments.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase),
                historyDatabasePath = _historyStore.DatabasePath,
                state = state
            };
        }

        private object BuildVpdlVersions()
        {
            var active = Program.ActiveVpdlInstallation;
            return new
            {
                ok = true,
                activeApiVersion = active == null ? "-" : active.ApiVersion,
                activeProductVersion = active == null ? "-" : active.ProductVersion,
                workerMode = Environment.GetEnvironmentVariable("VISIONQC_VPDL_WORKER_MODE") ?? "exact",
                selectedApiVersion = VpdlWorkerSelection.Read(),
                available = VpdlRuntimeCatalog.Discover().Select(item => new
                {
                    productVersion = item.ProductVersion,
                    apiVersion = item.ApiVersion,
                    displayName = item.DisplayName,
                    workerInstalled = VpdlWorkerLocator.IsAvailable(Program.AgentHomeDirectory, item.ApiVersion)
                }).ToArray()
            };
        }

        private object SelectVpdlWorker(string body)
        {
            var request = DeserializeDictionary(body);
            string requested = FirstNonEmpty(GetString(request, "apiVersion", ""), GetString(request, "version", ""));
            var target = VpdlRuntimeCatalog.FindByVersion(requested);
            if (target == null) return new { ok = false, error = "선택한 VPDL 버전이 정상 설치본으로 확인되지 않습니다: " + requested };
            if (_vpdlReservedForSimulation || HasAnyPreloadedRuntimeLocked())
                return new { ok = false, error = "Simulation 또는 Runtime File Load가 실행 중입니다. 완료 또는 중지 후 VPDL 버전을 전환하세요." };

            if (!VpdlWorkerLocator.IsAvailable(Program.AgentHomeDirectory, target.ApiVersion))
                return new { ok = false, error = "VPDL " + target.ProductVersion + " (API " + target.ApiVersion + ")용 Worker가 설치되어 있지 않습니다." };

            var active = Program.ActiveVpdlInstallation;
            if (active != null && string.Equals(active.ApiVersion, target.ApiVersion, StringComparison.OrdinalIgnoreCase))
                return new { ok = true, restarted = false, activeApiVersion = active.ApiVersion, message = "이미 선택된 VPDL Worker가 실행 중입니다." };

            try
            {
                VpdlWorkerSelection.Write(target.ApiVersion);
                string launcher = Path.Combine(Program.AgentHomeDirectory, "VisionQC.LocalAgent.exe");
                if (File.Exists(launcher))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = launcher,
                        Arguments = "--vpdl " + target.ApiVersion + " --delay 700",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Hidden
                    });
                }
                _ = Task.Run(async () =>
                {
                    await Task.Delay(250).ConfigureAwait(false);
                    Program.RequestWorkerRestart();
                    _serverCts.Cancel();
                });
                return new { ok = true, restarting = true, selectedApiVersion = target.ApiVersion, selectedVpdlVersion = target.ProductVersion };
            }
            catch (SysException ex)
            {
                return new { ok = false, error = "VPDL Worker 전환 준비 실패: " + ex.Message };
            }
        }
        private object RuntimeCheck(string body)
        {
            lock (_vpdlSync)
            {
                if (_vpdlReservedForSimulation)
                {
                    _licenseStatus = "Runtime Active";
                    _runtimeMessage = "Simulation Runtime 사용 중";
                    return new { ok = true, busy = true, license = _licenseStatus, gpu = _gpuName, installedVpdlVersion = _vpdlVersion, vpdlVersion = _vpdlVersion };
                }
                if (_preloadedRuntimeControl != null)
                {
                    _licenseStatus = "Runtime Ready";
                    _runtimeMessage = "Runtime File Load 완료 · Simulation 시작 대기";
                    return new { ok = true, preloaded = true, license = _licenseStatus, gpu = _gpuName, installedVpdlVersion = _vpdlVersion, vpdlVersion = _vpdlVersion, token = _preloadedRuntimeToken };
                }
                // Availability is not a license/inference test. The real preload validates those.
                // Repeated browser polling must never initialize or tear down CUDA contexts.
                return new { ok = true, deferred = true, licenseVerified = false,
                    message = "Agent 연결 정상 · GPU/License 확인은 Runtime File Load에서 진행합니다.",
                    license = _licenseStatus, gpu = _gpuName, installedVpdlVersion = _vpdlVersion, vpdlVersion = "-" };
            }
        }

        private RuntimePreloadResponse PreloadRuntime(string body)
        {
            AgentStartRequest req;
            try { req = _json.Deserialize<AgentStartRequest>(body ?? "{}"); }
            catch (SysException ex) { return new RuntimePreloadResponse { ok = false, error = "설정 JSON 오류: " + ex.Message }; }

            string validation = ValidateRuntimePreloadRequest(req);
            if (!string.IsNullOrWhiteSpace(validation))
                return new RuntimePreloadResponse { ok = false, error = validation };

            string mode = (req.mode ?? "green").Trim().ToLowerInvariant();
            string signature = BuildRuntimePreloadSignature(req);
            var sw = Stopwatch.StartNew();

            lock (_vpdlSync)
            {
                if (_vpdlReservedForSimulation)
                    return new RuntimePreloadResponse { ok = false, error = "Simulation이 실행 중입니다. 완료 또는 중지 후 Runtime File Load를 실행하세요." };

                DisposeInspectionControlLocked();
                DisposePreloadedRuntimeLocked();

                LocalRuntime.Control control = null;
                Dictionary<string, PositionWorkerClient> positionWorkers = null;
                try
                {
                    bool useGpu;
                    string gpuDevices;
                    if (mode == "green")
                    {
                        AgentGreenOptions green = GetGreenOptions(req);
                        useGpu = green.useGpu;
                        gpuDevices = green.gpuDevices;
                    }
                    else
                    {
                        AgentBlueOptions blue = GetBlueOptions(req);
                        useGpu = blue.useGpu;
                        gpuDevices = blue.gpuDevices;
                    }
                    var positions = EnabledPositions(req).ToList();
                    bool parallel = CanUseParallelPositionRuntime(req, positions.Count);
                    var assignments = BuildPositionGpuAssignments(req, positions, parallel);
                    var response = new RuntimePreloadResponse { ok = true, mode = mode, installedVpdlVersion = _vpdlVersion, vpdlVersion = _vpdlVersion, parallelPositions = parallel, positionGpuAssignments = assignments };
                    int total = positions.Count * (mode == "integrated" ? 2 : 1);
                    int completed = 0;
                    AppendAgentLog("INFO", "Runtime File Load 시작 | Mode=" + mode + " | Workspace " + total + " | Position Worker=" + (parallel ? positions.Count : 1));

                    if (parallel)
                    {
                        positionWorkers = new Dictionary<string, PositionWorkerClient>(StringComparer.OrdinalIgnoreCase);
                        foreach (var position in positions)
                        {
                            AppendAgentLog("INFO", "Position Worker 준비 | " + position.displayName + " | GPU=" + assignments[position.key]);
                            PositionWorkerClient worker = StartPositionWorker(position, assignments[position.key]);
                            positionWorkers[position.key] = worker;
                            AgentStartRequest childRequest = ClonePositionRequest(req, position, assignments);
                            RuntimePreloadResponse childResponse = WorkerPost<RuntimePreloadResponse>(worker, "/api/runtime/preload", childRequest, 15 * 60 * 1000);
                            if (childResponse == null || !childResponse.ok) throw new SysInvalidOperationException(position.displayName + " Worker Runtime Load 실패: " + (childResponse == null ? "응답 없음" : childResponse.error));
                            response.items.AddRange(childResponse.items ?? new List<RuntimePreloadItem>());
                            completed += childResponse.workspaceCount;
                            AppendAgentLog("INFO", "Runtime File Load 진행 " + completed + "/" + total + " | " + position.displayName);
                        }
                        RememberPreloadedPositionWorkersLocked(positionWorkers, assignments, req, signature);
                        positionWorkers = null;
                    }
                    else
                    {
                        var gpuList = ParseGpuList(gpuDevices, useGpu);
                        control = CreateRuntimeControl(req, mode, useGpu, gpuList, "Runtime File Load");
                        foreach (var position in positions)
                        {
                            if (mode != "blue")
                            {
                                string path = FirstNonEmpty(position.greenWorkspacePath, position.workspacePath);
                                response.items.Add(LoadPreloadedWorkspace(control, position, "green", "ws_" + position.key, path, mode == "green" && GetGreenOptions(req).detailedDiagnostics));
                                completed++;
                                AppendAgentLog("INFO", "Runtime File Load 진행 " + completed + "/" + total + " | " + position.displayName + " Green");
                            }
                            if (mode != "green")
                            {
                                string path = FirstNonEmpty(position.blueWorkspacePath, position.workspacePath);
                                response.items.Add(LoadPreloadedWorkspace(control, position, "blue", "blue_" + position.key, path));
                                completed++;
                                AppendAgentLog("INFO", "Runtime File Load 진행 " + completed + "/" + total + " | " + position.displayName + " Blue");
                            }
                        }
                        RememberPreloadedRuntimeLocked(control, req, signature);
                        control = null;
                    }
                    AgentDiagnostics.WriteLoadedLibraries();
                    _licenseStatus = "Runtime Ready";
                    _runtimeMessage = "Runtime File Load 완료 · Simulation 시작 대기";
                    response.token = _preloadedRuntimeToken;
                    response.signature = _preloadedRuntimeSignature;
                    response.controlSignature = _preloadedRuntimeControlSignature;
                    response.greenWorkspaceSignature = _preloadedGreenWorkspaceSignature;
                    response.workspaceCount = response.items.Count;
                    sw.Stop();
                    response.elapsedMs = sw.ElapsedMilliseconds;
                    AppendAgentLog("INFO", "Runtime File Load 완료 | Workspace " + response.workspaceCount + " | " + sw.Elapsed.TotalSeconds.ToString("0.0") + "초");
                    return response;
                }
                catch (SysException ex)
                {
                    try
                    {
                        if (control != null)
                        {
                            RuntimeWorkspaceRegistry.Remove(control);
                            control.Dispose();
                        }
                    }
                    catch { }
                        if (positionWorkers != null)
                        {
                            foreach (PositionWorkerClient failedWorker in positionWorkers.Values)
                            {
                                DisposePositionWorker(failedWorker);
                            }
                        }
                    DisposePreloadedRuntimeLocked();
                    sw.Stop();
                    _licenseStatus = "Runtime Error";
                    _runtimeMessage = ex.Message;
                    AppendAgentLog("ERROR", "Runtime File Load 실패: " + ex.Message);
                    AgentDiagnostics.Write("PRELOAD_ERROR", ex.ToString());
                    return new RuntimePreloadResponse { ok = false, mode = mode, error = ex.Message, elapsedMs = sw.ElapsedMilliseconds };
                }
            }
        }

        private RuntimePreloadItem LoadPreloadedWorkspace(LocalRuntime.Control control, AgentPositionRequest position, string kind, string workspaceName, string path, bool detailed = false)
        {
            AppendAgentLog("INFO", position.displayName + " " + kind.ToUpperInvariant() + " Runtime 로드 중: " + Path.GetFileName(path));
            ViDi2.Runtime.IWorkspace workspace = AgentDiagnostics.Measure("Workspace.Load", GreenRuntimeFactory.Describe(control) + " | Workspace=" + path, detailed,
                () => control.Workspaces.Add(workspaceName, path));
            // VPDL Workspaces 컬렉션은 버전에 따라 문자열 indexer가 동일하게 동작하지 않습니다.
            // Add가 반환한 실제 객체를 Control과 이름으로 직접 보관해 Simulation에서 재사용합니다.
            RuntimeWorkspaceRegistry.Register(control, workspaceName, workspace);
            WorkspaceInspectionResponse info = AgentDiagnostics.Measure("Workspace.Structure", GreenRuntimeFactory.Describe(control), detailed,
                () => BuildWorkspaceInspectionResult(workspace, path, "RuntimePreload"));
            if (detailed) AgentDiagnostics.CaptureEnvironment("after-workspace-load");
            return new RuntimePreloadItem
            {
                positionKey = position.key,
                displayName = position.displayName,
                kind = kind,
                info = info
            };
        }

        private LocalRuntime.Control CreateRuntimeControl(AgentStartRequest req, string mode, bool useGpu, List<int> gpuList, string reason)
        {
            var gpuMode = useGpu ? VpdlGpuMode.SingleDevicePerTool : VpdlGpuMode.NoSupport;
            AgentDiagnostics.Operation(reason + " | Mode=" + mode + " | GPU=" + useGpu + " | Devices=" + string.Join(",", gpuList));
            AgentDiagnostics.Write("GPU_LIFECYCLE", reason + " | Mode=" + mode + " | Devices=" + string.Join(",", gpuList));
            return mode == "green"
                ? GreenRuntimeFactory.Create(gpuMode, gpuList, GetGreenOptions(req).detailedDiagnostics, GetGreenOptions(req).disableOptimizedGpuMemory, reason)
                : new LocalRuntime.Control(gpuMode, gpuList);
        }

        private PositionWorkerClient StartPositionWorker(AgentPositionRequest position, string gpuAssignment)
        {
            int port = ReserveLoopbackPort();
            string home = Path.Combine(Path.GetTempPath(), "VisionQC-PositionWorker-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(home);
            var worker = new PositionWorkerClient { PositionKey = position.key, DisplayName = position.displayName, GpuAssignment = gpuAssignment, Port = port, HomePath = home };
            var start = new ProcessStartInfo
            {
                FileName = Application.ExecutablePath,
                Arguments = "--worker",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = home
            };
            start.EnvironmentVariables["VISIONQC_AGENT_PORT"] = port.ToString();
            start.EnvironmentVariables["VISIONQC_AGENT_CHILD"] = "1";
            start.EnvironmentVariables["VISIONQC_AGENT_PARENT_PID"] = Process.GetCurrentProcess().Id.ToString();
            start.EnvironmentVariables["VISIONQC_AGENT_HOME"] = home;
            start.EnvironmentVariables["VISIONQC_HISTORY_DB_PATH"] = Path.Combine(home, "history.sqlite");
            worker.Process = Process.Start(start);
            if (worker.Process == null) throw new SysInvalidOperationException(position.displayName + " Position Worker를 시작하지 못했습니다.");
            var deadline = DateTime.UtcNow.AddSeconds(45);
            while (DateTime.UtcNow < deadline)
            {
                if (worker.Process.HasExited) throw new SysInvalidOperationException(position.displayName + " Position Worker가 준비 중 종료되었습니다. ExitCode=" + worker.Process.ExitCode);
                try
                {
                    WorkerGet<Dictionary<string, object>>(worker, "/api/status", 2000);
                    return worker;
                }
                catch { Thread.Sleep(250); }
            }
            DisposePositionWorker(worker);
            throw new TimeoutException(position.displayName + " Position Worker 준비 시간이 초과되었습니다.");
        }

        private static int ReserveLoopbackPort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private T WorkerGet<T>(PositionWorkerClient worker, string path, int timeoutMs)
        {
            return WorkerRequest<T>(worker, path, null, timeoutMs);
        }

        private T WorkerPost<T>(PositionWorkerClient worker, string path, object body, int timeoutMs)
        {
            return WorkerRequest<T>(worker, path, body, timeoutMs);
        }

        private T WorkerRequest<T>(PositionWorkerClient worker, string path, object body, int timeoutMs)
        {
            var request = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + worker.Port + path);
            request.Method = body == null ? "GET" : "POST";
            request.Timeout = timeoutMs;
            request.ReadWriteTimeout = timeoutMs;
            if (body != null)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(_json.Serialize(body));
                request.ContentType = "application/json";
                request.ContentLength = bytes.Length;
                using (Stream stream = request.GetRequestStream()) stream.Write(bytes, 0, bytes.Length);
            }
            using (var response = (HttpWebResponse)request.GetResponse())
            using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                return _json.Deserialize<T>(reader.ReadToEnd());
        }

        private void DisposePositionWorker(PositionWorkerClient worker)
        {
            if (worker == null) return;
            try { WorkerPost<Dictionary<string, object>>(worker, "/api/agent/exit", new { }, 2000); } catch { }
            try
            {
                if (worker.Process != null && !worker.Process.WaitForExit(3000)) worker.Process.Kill();
                worker.Process?.Dispose();
            }
            catch { }
            try { if (Directory.Exists(worker.HomePath)) Directory.Delete(worker.HomePath, true); } catch { }
        }

        private object ConfigureParallelWorkerForwarding(string body)
        {
            var request = DeserializeDictionary(body);
            _forwardCoordinatorUrl = GetString(request, "coordinatorUrl", "");
            _forwardCoordinatorToken = GetString(request, "token", "");
            _forwardPositionKey = GetString(request, "positionKey", "");
            return new { ok = !string.IsNullOrWhiteSpace(_forwardCoordinatorUrl) && !string.IsNullOrWhiteSpace(_forwardCoordinatorToken) };
        }

        private object ReceiveParallelWorkerProgress(string body)
        {
            ParallelProgressEnvelope envelope;
            try { envelope = _json.Deserialize<ParallelProgressEnvelope>(body ?? "{}"); }
            catch (SysException ex) { return new { ok = false, error = ex.Message }; }
            if (envelope == null || !string.Equals(envelope.token, _parallelCoordinatorToken, StringComparison.Ordinal) || envelope.progress == null)
                return new { ok = false, error = "Position Worker progress token이 올바르지 않습니다." };
            string displayName;
            lock (_parallelProgressSync)
            {
                if (_parallelProgressStates == null || !_parallelProgressStates.ContainsKey(envelope.positionKey)) return new { ok = false, error = "Position Worker progress 대상이 없습니다." };
                displayName = _parallelPositionDisplayNames != null && _parallelPositionDisplayNames.ContainsKey(envelope.positionKey) ? _parallelPositionDisplayNames[envelope.positionKey] : envelope.positionKey;
            }
            OnParallelEngineProgress(envelope.positionKey, displayName, envelope.progress, _parallelProgressStates, _parallelProgressSync);
            return new { ok = true };
        }

        private void ForwardParallelProgress(ProcessProgress progress)
        {
            if (string.IsNullOrWhiteSpace(_forwardCoordinatorUrl) || string.IsNullOrWhiteSpace(_forwardCoordinatorToken) || progress == null) return;
            try
            {
                var request = (HttpWebRequest)WebRequest.Create(_forwardCoordinatorUrl.TrimEnd('/') + "/api/parallel/progress");
                request.Method = "POST";
                request.Timeout = 5000;
                request.ReadWriteTimeout = 5000;
                request.ContentType = "application/json";
                byte[] bytes = Encoding.UTF8.GetBytes(_json.Serialize(new ParallelProgressEnvelope { token = _forwardCoordinatorToken, positionKey = _forwardPositionKey, progress = progress }));
                request.ContentLength = bytes.Length;
                using (Stream stream = request.GetRequestStream()) stream.Write(bytes, 0, bytes.Length);
                using (var response = request.GetResponse()) { }
            }
            catch (SysException ex) { AgentDiagnostics.Write("PARALLEL_PROGRESS_FORWARD_ERROR", ex.Message); }
        }

        private Dictionary<string, string> BuildPositionGpuAssignments(AgentStartRequest req, List<AgentPositionRequest> positions, bool parallel)
        {
            string mode = (req.mode ?? "green").Trim().ToLowerInvariant();
            bool useGpu = mode == "green" ? GetGreenOptions(req).useGpu : GetBlueOptions(req).useGpu;
            string configured = mode == "green" ? GetGreenOptions(req).gpuDevices : GetBlueOptions(req).gpuDevices;
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!useGpu)
            {
                foreach (AgentPositionRequest position in positions) result[position.key] = "CPU";
                return result;
            }
            List<int> available = req.autoDistributeGpu && _gpuDeviceIndices.Count > 0
                ? _gpuDeviceIndices.ToList()
                : ParseGpuList(configured, true);
            if (!parallel)
            {
                string devices = string.Join(",", available);
                foreach (AgentPositionRequest position in positions) result[position.key] = devices;
                return result;
            }
            for (int index = 0; index < positions.Count; index++) result[positions[index].key] = available[index % available.Count].ToString();
            return result;
        }

        private bool CanUseParallelPositionRuntime(AgentStartRequest req, int positionCount)
        {
            if (req == null || !req.parallelPositions || positionCount <= 1) return false;
            if (string.Equals((req.mode ?? "green").Trim(), "green", StringComparison.OrdinalIgnoreCase))
            {
                AgentGreenOptions options = GetGreenOptions(req);
                if (options.originalProcess || options.freshRuntime || options.disableTensorRt)
                {
                    AppendAgentLog("WARN", "원본/호환 Runtime 옵션은 기존 직렬 실행으로 유지합니다. Position 병렬 실행은 표준 사전 로드 Runtime에서 사용됩니다.");
                    return false;
                }
            }
            return true;
        }

        private string ValidateRuntimePreloadRequest(AgentStartRequest req)
        {
            if (req == null) return "Simulation 설정이 없습니다.";
            string mode = (req.mode ?? "green").Trim().ToLowerInvariant();
            if (mode != "green" && mode != "blue" && mode != "integrated") return "Simulation Mode를 확인하세요.";
            var positions = EnabledPositions(req).ToList();
            if (positions.Count == 0) return "사용할 Position을 1개 이상 체크하세요.";
            foreach (var p in positions)
            {
                if (string.IsNullOrWhiteSpace(p.key)) return "Position Key가 비어 있습니다.";
                if (mode != "blue")
                {
                    string path = FirstNonEmpty(p.greenWorkspacePath, p.workspacePath);
                    if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return p.displayName + " Green Workspace를 확인하세요.";
                }
                if (mode != "green")
                {
                    string path = FirstNonEmpty(p.blueWorkspacePath, p.workspacePath);
                    if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return p.displayName + " Blue Workspace를 확인하세요.";
                }
            }
            return null;
        }

        private string BuildRuntimePreloadSignature(AgentStartRequest req)
        {
            string mode = (req.mode ?? "green").Trim().ToLowerInvariant();
            AgentGreenOptions green = GetGreenOptions(req);
            AgentBlueOptions blue = GetBlueOptions(req);
            var sb = new StringBuilder();
            sb.Append(mode).Append('|');
            if (mode == "green") sb.Append(green.useGpu).Append('|').Append(green.gpuDevices ?? "");
            else sb.Append(blue.useGpu).Append('|').Append(blue.gpuDevices ?? "");
            sb.Append("|P:").Append(req.parallelPositions).Append("|A:").Append(req.autoDistributeGpu);
            sb.Append(RuntimeDiagnosticSignature(req));
            foreach (var p in EnabledPositions(req).OrderBy(x => x.key, StringComparer.OrdinalIgnoreCase))
            {
                sb.Append('|').Append(p.key ?? "");
                if (mode != "blue") sb.Append("|G:").Append(NormalizeRuntimePath(FirstNonEmpty(p.greenWorkspacePath, p.workspacePath)));
                if (mode != "green") sb.Append("|B:").Append(NormalizeRuntimePath(FirstNonEmpty(p.blueWorkspacePath, p.workspacePath)));
            }
            return sb.ToString();
        }

        private string BuildRuntimeControlSignature(AgentStartRequest req)
        {
            string mode = (req.mode ?? "green").Trim().ToLowerInvariant();
            if (mode == "green")
            {
                AgentGreenOptions green = GetGreenOptions(req);
                return green.useGpu + "|" + (green.gpuDevices ?? "") + RuntimeDiagnosticSignature(req) + ParallelRuntimeSignature(req);
            }
            AgentBlueOptions blue = GetBlueOptions(req);
            return blue.useGpu + "|" + (blue.gpuDevices ?? "") + RuntimeDiagnosticSignature(req) + ParallelRuntimeSignature(req);
        }

        private string RuntimeDiagnosticSignature(AgentStartRequest req)
        {
            bool green = string.Equals(req.mode ?? "green", "green", StringComparison.OrdinalIgnoreCase);
            var options = GetGreenOptions(req);
            return "|D:" + (green && options.detailedDiagnostics) + "|M:" + (green && options.disableOptimizedGpuMemory);
        }

        private static string ParallelRuntimeSignature(AgentStartRequest req)
        {
            // Worker concurrency can change after preload because it does not alter a Runtime.
            return "|P:" + req.parallelPositions + "|A:" + req.autoDistributeGpu;
        }

        private static int NormalizeMaxParallel(AgentStartRequest req)
        {
            return Math.Max(1, Math.Min(10, req == null || req.maxParallelPositions <= 0 ? 10 : req.maxParallelPositions));
        }

        private string BuildGreenWorkspaceSignature(AgentStartRequest req)
        {
            var sb = new StringBuilder();
            foreach (var p in EnabledPositions(req).OrderBy(x => x.key, StringComparer.OrdinalIgnoreCase))
            {
                sb.Append('|').Append(p.key ?? "");
                sb.Append("|G:").Append(NormalizeRuntimePath(FirstNonEmpty(p.greenWorkspacePath, p.workspacePath)));
            }
            return sb.ToString();
        }

        private bool HasCompatiblePreloadedRuntime(AgentStartRequest req, string requestedSignature)
        {
            if (!HasAnyPreloadedRuntimeLocked()) return false;
            if (string.Equals(_preloadedRuntimeSignature, requestedSignature, StringComparison.Ordinal)) return true;

            string requestedMode = (req.mode ?? "green").Trim().ToLowerInvariant();
            return requestedMode == "green"
                && string.Equals(_preloadedRuntimeMode, "integrated", StringComparison.Ordinal)
                && string.Equals(_preloadedRuntimeControlSignature, BuildRuntimeControlSignature(req), StringComparison.Ordinal)
                && string.Equals(_preloadedGreenWorkspaceSignature, BuildGreenWorkspaceSignature(req), StringComparison.Ordinal);
        }

        private void RememberPreloadedRuntimeLocked(LocalRuntime.Control control, AgentStartRequest req, string signature)
        {
            _preloadedPositionWorkers.Clear();
            _preloadedPositionGpuAssignments.Clear();
            _preloadedRuntimeControl = control;
            _preloadedRuntimeSignature = signature;
            _preloadedRuntimeToken = Guid.NewGuid().ToString("N");
            _preloadedRuntimeMode = (req.mode ?? "green").Trim().ToLowerInvariant();
            _preloadedRuntimeControlSignature = BuildRuntimeControlSignature(req);
            _preloadedGreenWorkspaceSignature = BuildGreenWorkspaceSignature(req);
        }

        private void RememberPreloadedPositionWorkersLocked(Dictionary<string, PositionWorkerClient> workers, Dictionary<string, string> assignments, AgentStartRequest req, string signature)
        {
            _preloadedRuntimeControl = null;
            _preloadedPositionWorkers.Clear();
            foreach (var item in workers) _preloadedPositionWorkers[item.Key] = item.Value;
            _preloadedPositionGpuAssignments.Clear();
            foreach (var item in assignments) _preloadedPositionGpuAssignments[item.Key] = item.Value;
            _preloadedRuntimeSignature = signature;
            _preloadedRuntimeToken = Guid.NewGuid().ToString("N");
            _preloadedRuntimeMode = (req.mode ?? "green").Trim().ToLowerInvariant();
            _preloadedRuntimeControlSignature = BuildRuntimeControlSignature(req);
            _preloadedGreenWorkspaceSignature = BuildGreenWorkspaceSignature(req);
        }

        private bool HasAnyPreloadedRuntimeLocked()
        {
            return _preloadedRuntimeControl != null || _preloadedPositionWorkers.Count > 0;
        }

        private static string NormalizeRuntimePath(string path)
        {
            try { return Path.GetFullPath(path ?? "").TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToUpperInvariant(); }
            catch { return (path ?? "").Trim().ToUpperInvariant(); }
        }

        private void DisposePreloadedRuntimeLocked()
        {
            if (_preloadedRuntimeControl != null)
            {
                try
                {
                    RuntimeWorkspaceRegistry.Remove(_preloadedRuntimeControl);
                    _preloadedRuntimeControl.Dispose();
                }
                catch { }
            }
            foreach (PositionWorkerClient worker in _preloadedPositionWorkers.Values) DisposePositionWorker(worker);
            _preloadedRuntimeControl = null;
            _preloadedPositionWorkers.Clear();
            _preloadedPositionGpuAssignments.Clear();
            _preloadedRuntimeSignature = "";
            _preloadedRuntimeToken = "";
            _preloadedRuntimeMode = "";
            _preloadedRuntimeControlSignature = "";
            _preloadedGreenWorkspaceSignature = "";
        }

        private WorkspaceInspectionResponse InspectWorkspace(string body)
        {
            var req = DeserializeDictionary(body);
            string path = GetString(req, "path", "");
            bool useGpu = GetBool(req, "useGpu", true);
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return WorkspaceInspectionFailure(path, "Runtime Workspace 파일을 확인하세요.", "Validation");

            var gpuList = ParseGpuList(GetString(req, "gpuDevices", "0"), useGpu);
            var mode = useGpu ? VpdlGpuMode.SingleDevicePerTool : VpdlGpuMode.NoSupport;
            var file = new FileInfo(path);
            string cacheKey = WorkspaceInspectionCacheKey(path, useGpu, gpuList);
            lock (_vpdlSync)
            {
                WorkspaceInspectionCacheEntry cached;
                if (_workspaceInspectionCache.TryGetValue(cacheKey, out cached) &&
                    cached.Length == file.Length && cached.LastWriteUtc == file.LastWriteTimeUtc)
                {
                    AppendAgentLog("INFO", "Workspace 구조 캐시 사용: " + Path.GetFileName(path));
                    return cached.Result;
                }
                if (_vpdlReservedForSimulation)
                    return new WorkspaceInspectionResponse
                    {
                        ok = false,
                        busy = true,
                        error = "Simulation이 실행 중입니다. 완료 또는 중지 후 Workspace 구조를 다시 확인하세요.",
                        path = path,
                        workspaceName = Path.GetFileName(path),
                        loadMethod = "RuntimeBusy",
                        streams = new List<WorkspaceInspectionStream>(),
                        warnings = new List<string>()
                    };
                if (_preloadedRuntimeControl != null)
                    return new WorkspaceInspectionResponse
                    {
                        ok = false,
                        busy = true,
                        error = "Runtime File Load 세션이 준비되어 있습니다. Workspace 경로를 바꿨다면 Runtime File Load를 다시 실행하세요.",
                        path = path,
                        workspaceName = Path.GetFileName(path),
                        loadMethod = "RuntimePreloaded",
                        streams = new List<WorkspaceInspectionStream>(),
                        warnings = new List<string>()
                    };

                bool firstDeferred = false;
                string firstMethod = "EnginePath";
                string firstError = null;
                try
                {
                    LocalRuntime.Control control = EnsureInspectionControl(useGpu, mode, gpuList, false);
                    firstDeferred = _inspectionControlDeferred;
                    firstMethod = firstDeferred ? "DeferredFileStream" : "EnginePath";
                    WorkspaceInspectionResponse result = LoadWorkspaceInspection(control, path, firstDeferred, null);
                    _workspaceInspectionCache[cacheKey] = new WorkspaceInspectionCacheEntry { Length = file.Length, LastWriteUtc = file.LastWriteTimeUtc, Result = result };
                    return result;
                }
                catch (SysException ex)
                {
                    firstError = ex.GetType().Name + ": " + ex.Message;
                    AppendAgentLog("WARN", "Workspace 구조 1차 읽기 실패(" + firstMethod + "), 반대 방식 재시도: " + Path.GetFileName(path) + " | " + firstError);
                    DisposeInspectionControlLocked();
                }

                bool fallbackDeferred = !firstDeferred;
                string fallbackMethod = fallbackDeferred ? "DeferredFileStream" : "EnginePath";
                try
                {
                    LocalRuntime.Control control = EnsureInspectionControl(useGpu, mode, gpuList, fallbackDeferred);
                    WorkspaceInspectionResponse result = LoadWorkspaceInspection(control, path, fallbackDeferred, firstError);
                    _workspaceInspectionCache[cacheKey] = new WorkspaceInspectionCacheEntry { Length = file.Length, LastWriteUtc = file.LastWriteTimeUtc, Result = result };
                    return result;
                }
                catch (SysException ex)
                {
                    string fallbackError = ex.GetType().Name + ": " + ex.Message;
                    string combined = firstMethod + "=" + (firstError ?? "-") + " | " + fallbackMethod + "=" + fallbackError;
                    AppendAgentLog("ERROR", "Workspace 구조 읽기 실패: " + Path.GetFileName(path) + " | " + combined);
                    DisposeInspectionControlLocked();
                    return WorkspaceInspectionFailure(path, combined, firstMethod + " -> " + fallbackMethod);
                }
            }
        }

        private LocalRuntime.Control EnsureInspectionControl(bool useGpu, VpdlGpuMode mode, List<int> gpuList, bool deferred)
        {
            string controlKey = (useGpu ? "GPU:" + string.Join(",", gpuList) : "CPU");
            if (_inspectionControl != null && string.Equals(_inspectionControlKey, controlKey, StringComparison.OrdinalIgnoreCase))
                return _inspectionControl;

            DisposeInspectionControlLocked();
            if (deferred)
            {
                _inspectionControl = new LocalRuntime.Control(new LocalRuntime.LibraryAccess(), VpdlGpuMode.Deferred, new List<int>(), false);
                if (useGpu) _inspectionControl.InitializeComputeDevices(mode, gpuList);
            }
            else
                _inspectionControl = new LocalRuntime.Control(mode, gpuList);
            _inspectionControlKey = controlKey;
            _inspectionControlDeferred = deferred;
            return _inspectionControl;
        }

        private WorkspaceInspectionResponse LoadWorkspaceInspection(LocalRuntime.Control control, string path, bool deferred, string previousWarning)
        {
            string workspaceName = "visionqc_inspect_" + Guid.NewGuid().ToString("N");
            ViDi2.Runtime.IWorkspace workspace;
            if (deferred)
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                    workspace = control.Workspaces.Add(workspaceName, fs);
            }
            else
                workspace = control.Workspaces.Add(workspaceName, path);
            return BuildWorkspaceInspectionResult(workspace, path, deferred ? "DeferredFileStream" : "EnginePath", previousWarning);
        }

        private static string WorkspaceInspectionCacheKey(string path, bool useGpu, List<int> gpuList)
        {
            string fullPath;
            try { fullPath = Path.GetFullPath(path ?? ""); }
            catch { fullPath = path ?? ""; }
            return fullPath + "|" + (useGpu ? "GPU:" + string.Join(",", gpuList) : "CPU");
        }

        private static WorkspaceInspectionResponse WorkspaceInspectionFailure(string path, string error, string loadMethod)
        {
            return new WorkspaceInspectionResponse
            {
                ok = false,
                error = error,
                path = path ?? "",
                workspaceName = Path.GetFileName(path ?? ""),
                loadMethod = loadMethod,
                streams = new List<WorkspaceInspectionStream>(),
                warnings = new List<string>()
            };
        }

        private void DisposeInspectionControlLocked()
        {
            try
            {
                var disposable = _inspectionControl as IDisposable;
                if (disposable != null) disposable.Dispose();
            }
            catch { }
            _inspectionControl = null;
            _inspectionControlKey = "";
            _inspectionControlDeferred = false;
        }

        private WorkspaceInspectionResponse BuildWorkspaceInspectionResult(ViDi2.Runtime.IWorkspace workspace, string path, string loadMethod, string previousWarning = null)
        {
            var streams = new List<WorkspaceInspectionStream>();
            var warnings = new List<string>();
            if (!string.IsNullOrWhiteSpace(previousWarning)) warnings.Add("1차 로딩 실패 후 fallback 성공: " + previousWarning);
            foreach (ViDi2.IStream stream in workspace.Streams)
            {
                var tools = new List<WorkspaceInspectionTool>();
                try
                {
                    foreach (ViDi2.ITool tool in stream.Tools)
                        CollectWorkspaceTool(tool, "", tools, warnings);
                }
                catch (SysException ex)
                {
                    warnings.Add("Stream '" + stream.Name + "' Tool 열거 실패: " + ex.Message);
                }
                streams.Add(new WorkspaceInspectionStream { name = stream.Name, tools = tools });
            }
            int toolCount = streams.Sum(x => x.tools == null ? 0 : x.tools.Count);
            AppendAgentLog("INFO", "Workspace 구조 읽기 완료: " + Path.GetFileName(path) +
                " | Method=" + loadMethod + " | Stream " + streams.Count + " | Tool " + toolCount);
            return new WorkspaceInspectionResponse
            {
                ok = true,
                path = path,
                workspaceName = Path.GetFileName(path),
                loadMethod = loadMethod,
                streamCount = streams.Count,
                toolCount = toolCount,
                streams = streams,
                warnings = warnings
            };
        }

        private static void CollectWorkspaceTool(ViDi2.ITool tool, string parentPath, List<WorkspaceInspectionTool> output, List<string> warnings)
        {
            if (tool == null || output == null) return;
            string name = "";
            string type = "Unknown";
            try { name = tool.Name ?? ""; } catch { }
            try { type = tool.Type.ToString(); } catch { }
            string toolPath = string.IsNullOrWhiteSpace(parentPath) ? name : parentPath + "/" + name;
            var info = new WorkspaceInspectionTool
            {
                name = name,
                path = toolPath,
                type = type,
                tags = ReadKnownNames(tool, "KnownTags"),
                classes = ReadKnownNames(tool, "KnownClasses"),
                features = ReadKnownNames(tool, "KnownFeatures")
            };
            output.Add(info);

            // 일부 VPDL Tool은 Children 열거가 직접 interface 접근에서 예외를 낼 수 있어
            // BeadGridInspector와 동일하게 dynamic + 개별 try/catch로 재귀 탐색한다.
            try
            {
                dynamic dyn = tool;
                foreach (object childObj in dyn.Children)
                {
                    var child = childObj as ViDi2.ITool;
                    if (child != null) CollectWorkspaceTool(child, toolPath, output, warnings);
                }
            }
            catch (SysException ex)
            {
                if (warnings != null) warnings.Add("Tool '" + toolPath + "' Child 열거 실패: " + ex.Message);
            }
        }

        private static List<string> ReadKnownNames(object source, string propertyName)
        {
            var result = new List<string>();
            if (source == null || string.IsNullOrWhiteSpace(propertyName)) return result;
            try
            {
                System.Reflection.PropertyInfo property = source.GetType().GetProperty(propertyName);
                if (property == null)
                {
                    foreach (Type iface in source.GetType().GetInterfaces())
                    {
                        property = iface.GetProperty(propertyName);
                        if (property != null) break;
                    }
                }
                object raw = property == null ? null : property.GetValue(source, null);
                var enumerable = raw as IEnumerable;
                if (enumerable == null) return result;
                foreach (object item in enumerable)
                {
                    if (item == null) continue;
                    string name = null;
                    try
                    {
                        var nameProperty = item.GetType().GetProperty("Name");
                        if (nameProperty == null)
                        {
                            foreach (Type iface in item.GetType().GetInterfaces())
                            {
                                nameProperty = iface.GetProperty("Name");
                                if (nameProperty != null) break;
                            }
                        }
                        object value = nameProperty == null ? null : nameProperty.GetValue(item, null);
                        name = value == null ? null : Convert.ToString(value);
                    }
                    catch { }
                    if (string.IsNullOrWhiteSpace(name)) name = Convert.ToString(item);
                    if (!string.IsNullOrWhiteSpace(name) && !result.Contains(name, StringComparer.OrdinalIgnoreCase))
                        result.Add(name);
                }
            }
            catch { }
            return result;
        }

        private object StartPicker(string body) { return _picker.Start(DeserializeDictionary(body)); }
        private object PickerStatus(string body) { return _picker.Status(DeserializeDictionary(body)); }
        private object PickFolder(string body) { return _picker.PickFolder(DeserializeDictionary(body)); }
        private object PickFile(string body) { return _picker.PickFile(DeserializeDictionary(body)); }
        private object CancelPicker(string body) { return _picker.Cancel(DeserializeDictionary(body)); }

        private object PreviewBlueFallback(string body)
        {
            var data = DeserializeDictionary(body);
            string path = GetString(data, "sampleImagePath", "");
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return new { ok = false, error = "샘플 이미지 파일을 확인하세요." };
            try
            {
                int cropW = GetInt(data, "cropWidth", 2448);
                int cropH = GetInt(data, "cropHeight", 2048);
                int fx = GetInt(data, "fallbackShiftX", 0);
                int fy = GetInt(data, "fallbackShiftY", 200);
                int rx = GetInt(data, "previewRoiX", 400);
                int ry = GetInt(data, "previewRoiY", 570);
                int rw = GetInt(data, "previewRoiW", 1658);
                int rh = GetInt(data, "previewRoiH", 589);
                using (var src = new System.Drawing.Bitmap(path))
                using (var original = BluePreviewHelper.DrawFallbackRect(src, cropW, cropH, fx, fy))
                using (var crop = BluePreviewHelper.CropFallback(src, cropW, cropH, fx, fy))
                using (var cropWithRoi = BluePreviewHelper.DrawRoiRect(crop, new System.Drawing.Rectangle(rx, ry, rw, rh)))
                using (var roi = BluePreviewHelper.CropRoi(crop, new System.Drawing.Rectangle(rx, ry, rw, rh)))
                {
                    return new
                    {
                        ok = true,
                        original = BitmapToDataUrl(original),
                        crop = BitmapToDataUrl(cropWithRoi),
                        roi = BitmapToDataUrl(roi)
                    };
                }
            }
            catch (SysException ex)
            {
                return new { ok = false, error = ex.Message };
            }
        }

        private static string BitmapToDataUrl(System.Drawing.Bitmap bmp)
        {
            using (var ms = new MemoryStream())
            {
                bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Jpeg);
                return "data:image/jpeg;base64," + Convert.ToBase64String(ms.ToArray());
            }
        }

        private object StartSimulation(string body)
        {
            AgentDiagnostics.SaveText("last-simulation-request.json", body);
            lock (_sync)
            {
                if (_simulationTask != null && !_simulationTask.IsCompleted)
                    return new { ok = false, error = "Simulation이 이미 실행 중입니다." };
            }

            AgentStartRequest req;
            try { req = _json.Deserialize<AgentStartRequest>(body ?? "{}"); }
            catch (SysException ex) { return new { ok = false, error = "설정 JSON 오류: " + ex.Message }; }

            string validation = ValidateRequest(req);
            if (!string.IsNullOrEmpty(validation)) return new { ok = false, error = validation };

            LocalRuntime.Control simulationControl;
            Dictionary<string, PositionWorkerClient> positionWorkers = null;
            Dictionary<string, string> positionGpuAssignments = null;
            lock (_vpdlSync)
            {
                if (_vpdlReservedForSimulation)
                    return new { ok = false, error = "다른 VPDL 작업이 실행 중입니다. 잠시 후 다시 시도하세요." };
                string signature = BuildRuntimePreloadSignature(req);
                if (!HasCompatiblePreloadedRuntime(req, signature))
                    return new { ok = false, error = "현재 설정에 맞는 Runtime 사전 로드가 없습니다. Workspace Runtime Structure의 Runtime File Load를 다시 실행하세요." };
                _vpdlReservedForSimulation = true;
                simulationControl = _preloadedRuntimeControl;
                if (_preloadedPositionWorkers.Count > 0)
                {
                    positionWorkers = _preloadedPositionWorkers.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
                    positionGpuAssignments = _preloadedPositionGpuAssignments.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
                    _preloadedPositionWorkers.Clear();
                    _preloadedPositionGpuAssignments.Clear();
                }
                _preloadedRuntimeControl = null;
                _preloadedRuntimeSignature = "";
                _preloadedRuntimeToken = "";
                _preloadedRuntimeMode = "";
                _preloadedRuntimeControlSignature = "";
                _preloadedGreenWorkspaceSignature = "";
                DisposeInspectionControlLocked();
            }

            try
            {
                lock (_sync)
                {
                    _liveBuffer.Clear();
                    _liveBatchSize = GetLiveBatchSize(req);
                    _liveRecordCount = 0;
                    _simulationStartedUtc = DateTime.UtcNow;
                    _lastProgressBroadcast = DateTime.MinValue;
                    _lastProgressValue = -1;
                }
                try { if (_simulationCts != null) _simulationCts.Dispose(); } catch { }
                _simulationCts = new CancellationTokenSource();
                lock (_sync)
                {
                    _state = NewIdleState();
                    _state.running = true;
                    _state.mode = (req.mode ?? "green").ToLowerInvariant();
                    _state.outputRoot = req.outputRoot;
                    _state.message = "Simulation 시작 준비 중...";
                }
                StartSimulationHistory(req);
                AppendAgentLog("START", "Simulation 시작 | Mode=" + _state.mode + " | Batch=" + _liveBatchSize + " | Output=" + (req.outputRoot ?? ""));
                Broadcast("progress", Snapshot(), true);
                _simulationTask = Task.Run(() => RunSimulation(req, simulationControl, positionWorkers, positionGpuAssignments, _simulationCts.Token));
                return new { ok = true, state = Snapshot() };
            }
            catch (SysException ex)
            {
                CompleteSimulationHistory("failed", "Simulation 시작 실패: " + ex.Message);
                lock (_vpdlSync)
                {
                    try
                    {
                        if (simulationControl != null)
                        {
                            RuntimeWorkspaceRegistry.Remove(simulationControl);
                            simulationControl.Dispose();
                        }
                        if (positionWorkers != null)
                        {
                            foreach (PositionWorkerClient positionWorker in positionWorkers.Values) DisposePositionWorker(positionWorker);
                        }
                    }
                    catch { }
                    _vpdlReservedForSimulation = false;
                }
                lock (_sync)
                {
                    _state.running = false;
                    _state.error = ex.ToString();
                    _state.message = "Simulation 시작 실패: " + ex.Message;
                }
                AppendAgentLog("ERROR", _state.message);
                AgentDiagnostics.Write("SIMULATION_ERROR", ex.ToString());
                return new { ok = false, error = _state.message };
            }
        }

        private object StopSimulation()
        {
            if (_simulationCts == null) return new { ok = true, message = "실행 중인 Simulation이 없습니다." };
            try { _simulationCts.Cancel(); } catch { }
            lock (_sync) _state.message = "중지 요청 전달됨...";
            Broadcast("progress", Snapshot(), true);
            return new { ok = true };
        }

        private void RunSimulation(AgentStartRequest req, LocalRuntime.Control simulationControl, Dictionary<string, PositionWorkerClient> positionWorkers, Dictionary<string, string> positionGpuAssignments, CancellationToken token)
        {
            bool runtimeReusable = true;
            try
            {
                if (positionWorkers != null && positionWorkers.Count > 1)
                {
                    RunParallelPositionSimulation(req, positionWorkers, positionGpuAssignments, token);
                    lock (_sync) _state.running = false;
                    FlushLiveBatch();
                    CompleteSimulationHistory("completed", _state.message);
                    AppendAgentLog("DONE", _state.message + " | Result=" + (_state.resultCsv ?? ""));
                    Broadcast("completed", Snapshot(), true);
                    return;
                }
                var progress = new DirectProgress<ProcessProgress>(p => OnEngineProgress(p));
                string mode = (req.mode ?? "green").Trim().ToLowerInvariant();
                if (mode == "blue")
                {
                    var summary = BlueCropProcessor.Run(BuildBlueConfig(req, req.outputRoot, false), simulationControl, true, progress, token);
                    lock (_sync)
                    {
                        _state.processed = summary.ProcessedImages;
                        _state.total = summary.TotalImages;
                        _state.message = string.Format("Blue 완료 | Saved {0} / Error {1}", summary.SavedImages, summary.ErrorCount);
                    }
                }
                else if (mode == "integrated")
                {
                    var integratedOptions = GetIntegratedOptions(req);
                    bool keepCropImages = integratedOptions.keepCropImages;
                    string cropRoot = PositionCropRoot(req, keepCropImages);
                    var blue = BuildBlueConfig(req, cropRoot, true);
                    var green = BuildGreenConfig(req, req.outputRoot, cropRoot, true);
                    var summary = IntegratedSimulationProcessor.RunStreaming(blue, green, keepCropImages, cropRoot, simulationControl, true, progress, token);
                    lock (_sync)
                    {
                        _state.processed = summary.BlueSummary.ProcessedImages;
                        _state.total = summary.BlueSummary.TotalImages;
                        _state.ok = summary.GreenSummary.TotalOkCount;
                        _state.ng = summary.GreenSummary.TotalNgCount;
                        _state.resultCsv = summary.GreenSummary.CsvPath;
                        _state.message = "Integrated Simulation 완료";
                    }
                }
                else
                {
                    var greenOptions = GetGreenOptions(req);
                    var greenConfig = BuildGreenConfig(req, req.outputRoot, null, false);
                    ProcessSummary summary;
                    if (greenOptions.originalProcess)
                    {
                        runtimeReusable = false;
                        AppendAgentLog("INFO", "[ORIGINAL] 사전 로드 객체를 넘기지 않고 독립 프로세스에서 원본 방식으로 검사합니다. TensorRT/메모리 변경 옵션은 적용하지 않습니다.");
                        RuntimeWorkspaceRegistry.Remove(simulationControl);
                        var previewControl = simulationControl;
                        simulationControl = null;
                        previewControl.Dispose();
                        token.ThrowIfCancellationRequested();
                        summary = GreenProcessHost.Run(greenConfig, progress, token,
                            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "VisionQC.GreenRunner.exe"),
                            Program.AgentHomeDirectory, Program.OriginalProcessPath);
                    }
                    else
                    {
                    greenConfig.DisableTensorRt = greenOptions.disableTensorRt;
                    // Never reuse a runtime whose in-memory inference settings were changed.
                    runtimeReusable = !greenOptions.disableTensorRt && !greenOptions.freshRuntime;
                    AgentDiagnostics.Write("GREEN_EXECUTION", "FreshRuntime=" + greenOptions.freshRuntime
                        + " | DisableTensorRT=" + greenOptions.disableTensorRt + " | GPU=" + greenOptions.useGpu
                        + " | Devices=" + greenOptions.gpuDevices + " | Thread=" + Thread.CurrentThread.ManagedThreadId);
                    if (greenOptions.freshRuntime)
                    {
                        AppendAgentLog("INFO", "[COMPAT] 검사 스레드에서 Runtime을 새로 엽니다. 기존 Workspace 파일은 변경하지 않습니다.");
                        RuntimeWorkspaceRegistry.Remove(simulationControl);
                        var oldControl = simulationControl;
                        simulationControl = null;
                        oldControl.Dispose();
                        token.ThrowIfCancellationRequested();
                        simulationControl = GreenRuntimeFactory.Create(greenConfig.UseGpu ? VpdlGpuMode.SingleDevicePerTool : VpdlGpuMode.NoSupport,
                            greenConfig.UseGpu ? greenConfig.GpuDevices : new List<int>(), greenConfig.DetailedDiagnostics, greenConfig.DisableOptimizedGpuMemory, "Fresh Runtime");
                    }
                    summary = GreenOverlayProcessor.Run(greenConfig, simulationControl, !greenOptions.freshRuntime, progress, token);
                    }
                    lock (_sync)
                    {
                        _state.processed = summary.TotalImages;
                        _state.total = summary.TotalImages;
                        _state.ok = summary.TotalOkCount;
                        _state.ng = summary.TotalNgCount;
                        _state.resultCsv = summary.CsvPath;
                        _state.message = "Green Simulation 완료";
                    }
                }
                lock (_sync) _state.running = false;
                FlushLiveBatch();
                CompleteSimulationHistory("completed", _state.message);
                AppendAgentLog("DONE", _state.message + " | Result=" + (_state.resultCsv ?? ""));
                Broadcast("completed", Snapshot(), true);
            }
            catch (OperationCanceledException)
            {
                lock (_sync)
                {
                    _state.running = false;
                    _state.activePositionWorkers = 0;
                    _state.message = "사용자에 의해 중지되었습니다.";
                }
                FlushLiveBatch();
                CompleteSimulationHistory("stopped", _state.message);
                AppendAgentLog("STOP", _state.message);
                Broadcast("stopped", Snapshot(), true);
            }
            catch (SysException ex)
            {
                runtimeReusable = false;
                lock (_sync)
                {
                    _state.running = false;
                    _state.activePositionWorkers = 0;
                    _state.error = ex.ToString();
                    _state.message = "Simulation 오류: " + ex.Message;
                }
                FlushLiveBatch();
                CompleteSimulationHistory("failed", _state.message);
                AppendAgentLog("ERROR", _state.message);
                Broadcast("error", Snapshot(), true);
                AgentDiagnostics.Write("SIMULATION_ERROR", ex.ToString());
            }
            finally
            {
                if (positionWorkers != null && positionWorkers.Count > 1) CleanupParallelTemporaryCrops(req);
                lock (_vpdlSync)
                {
                    if (runtimeReusable && simulationControl != null)
                    {
                        DisposePreloadedRuntimeLocked();
                        RememberPreloadedRuntimeLocked(simulationControl, req, BuildRuntimePreloadSignature(req));
                        _licenseStatus = "Runtime Ready";
                        _runtimeMessage = "Simulation 완료 · 사전 로드 Runtime 재사용 가능";
                        simulationControl = null;
                    }
                    else if (runtimeReusable && positionWorkers != null && positionWorkers.Count > 0)
                    {
                        DisposePreloadedRuntimeLocked();
                        RememberPreloadedPositionWorkersLocked(positionWorkers, positionGpuAssignments ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), req, BuildRuntimePreloadSignature(req));
                        _licenseStatus = "Runtime Ready";
                        _runtimeMessage = "병렬 Simulation 완료 · Position Runtime 재사용 가능";
                        positionWorkers = null;
                    }
                    else
                    {
                        try
                        {
                            if (simulationControl != null)
                            {
                                RuntimeWorkspaceRegistry.Remove(simulationControl);
                                AgentDiagnostics.Write("RUNTIME_DISPOSE", "Simulation runtime disposal started; inference failure, if any, is preserved separately");
                                simulationControl.Dispose();
                            }
                        }
                        catch { }
                        if (positionWorkers != null)
                        {
                            foreach (PositionWorkerClient positionWorker in positionWorkers.Values) DisposePositionWorker(positionWorker);
                        }
                        _runtimeMessage = "Runtime File Load를 다시 실행하세요. 진단/호환 검사 또는 오류 후에는 Runtime을 재사용하지 않습니다.";
                    }
                    _vpdlReservedForSimulation = false;
                }
            }
        }

        private sealed class PositionProgressState
        {
            public int Processed;
            public int Total;
            public int Ok;
            public int Ng;
        }

        private sealed class PositionRunResult
        {
            public string PositionKey;
            public string DisplayName;
            public int Processed;
            public int Total;
            public int Ok;
            public int Ng;
            public string CsvPath;
        }

        private sealed class PositionWorkerClient
        {
            public string PositionKey;
            public string DisplayName;
            public string GpuAssignment;
            public int Port;
            public string HomePath;
            public Process Process;
        }

        private sealed class ParallelProgressEnvelope
        {
            public string token { get; set; }
            public string positionKey { get; set; }
            public ProcessProgress progress { get; set; }
        }

        private sealed class WorkerApiResponse
        {
            public bool ok { get; set; }
            public bool running { get; set; }
            public string error { get; set; }
            public SimulationState state { get; set; }
        }

        private void RunParallelPositionSimulation(AgentStartRequest req, Dictionary<string, PositionWorkerClient> workers, Dictionary<string, string> assignments, CancellationToken token)
        {
            var positions = EnabledPositions(req).ToList();
            int maxParallel = Math.Min(positions.Count, NormalizeMaxParallel(req));
            var progressStates = positions.ToDictionary(x => x.key, x => new PositionProgressState(), StringComparer.OrdinalIgnoreCase);
            var gate = new SemaphoreSlim(maxParallel, maxParallel);
            var requests = positions.ToDictionary(position => position.key, position => ClonePositionRequest(req, position, assignments), StringComparer.OrdinalIgnoreCase);
            string coordinatorToken = Guid.NewGuid().ToString("N");
            lock (_parallelProgressSync)
            {
                _parallelCoordinatorToken = coordinatorToken;
                _parallelProgressStates = progressStates;
                _parallelPositionDisplayNames = positions.ToDictionary(x => x.key, x => x.displayName, StringComparer.OrdinalIgnoreCase);
            }
            foreach (AgentPositionRequest position in positions)
            {
                WorkerApiResponse configured = WorkerPost<WorkerApiResponse>(workers[position.key], "/api/parallel/configure", new
                {
                    coordinatorUrl = "http://127.0.0.1:" + _port,
                    token = coordinatorToken,
                    positionKey = position.key
                }, 5000);
                if (configured == null || !configured.ok)
                    throw new SysInvalidOperationException(position.displayName + " Position Worker 진행 연결에 실패했습니다: " + (configured == null ? "응답 없음" : configured.error));
            }
            lock (_sync)
            {
                _state.activePositionWorkers = maxParallel;
                _state.completedPositionWorkers = 0;
                _state.message = "Position 병렬 Simulation 시작 | " + positions.Count + "개 / 동시 " + maxParallel + "개";
            }
            AppendAgentLog("INFO", _state.message);

            var tasks = positions.Select(position => Task.Run(() =>
            {
                gate.Wait(token);
                try
                {
                    token.ThrowIfCancellationRequested();
                    string gpu = assignments != null && assignments.ContainsKey(position.key) ? assignments[position.key] : "-";
                    AppendAgentLog("INFO", "Position Worker 시작 | " + position.displayName + " | GPU=" + gpu);
                    PositionWorkerClient worker = workers[position.key];
                    WorkerApiResponse started = WorkerPost<WorkerApiResponse>(worker, "/api/simulation/start", requests[position.key], 30000);
                    if (started == null || !started.ok)
                        throw new SysInvalidOperationException(position.displayName + " Position Worker 시작 실패: " + (started == null ? "응답 없음" : started.error));
                    WorkerApiResponse status = started;
                    try
                    {
                        do
                        {
                            token.ThrowIfCancellationRequested();
                            Thread.Sleep(250);
                            status = WorkerGet<WorkerApiResponse>(worker, "/api/status", 5000);
                        }
                        while (status != null && status.running);
                    }
                    catch (OperationCanceledException)
                    {
                        try { WorkerPost<WorkerApiResponse>(worker, "/api/simulation/stop", new { }, 5000); } catch { }
                        throw;
                    }
                    if (status == null || !status.ok || status.state == null)
                        throw new SysInvalidOperationException(position.displayName + " Position Worker 상태를 읽지 못했습니다: " + (status == null ? "응답 없음" : status.error));
                    if (!string.IsNullOrWhiteSpace(status.state.error))
                        throw new SysInvalidOperationException(position.displayName + " Position Worker 오류: " + status.state.error);
                    PositionRunResult result = new PositionRunResult
                    {
                        PositionKey = position.key, DisplayName = position.displayName,
                        Processed = status.state.processed, Total = status.state.total,
                        Ok = status.state.ok, Ng = status.state.ng, CsvPath = status.state.resultCsv
                    };
                    lock (_sync)
                    {
                        _state.completedPositionWorkers++;
                        _state.message = "Position 완료 " + _state.completedPositionWorkers + "/" + positions.Count + " | " + position.displayName;
                    }
                    AppendAgentLog("DONE", "Position Worker 완료 | " + position.displayName + " | " + result.Processed + "건");
                    Broadcast("progress", Snapshot(), true);
                    return result;
                }
                finally { gate.Release(); }
            }, token)).ToArray();

            try
            {
                try { Task.WaitAll(tasks); }
                catch (AggregateException ex)
                {
                    var actual = ex.Flatten().InnerExceptions.FirstOrDefault(error => !(error is OperationCanceledException)) ?? ex.Flatten().InnerExceptions.First();
                    if (actual is OperationCanceledException) throw new OperationCanceledException(token);
                    throw actual;
                }
                var results = tasks.Select(task => task.Result).ToList();
                string mode = (req.mode ?? "green").Trim().ToLowerInvariant();
                lock (_sync)
                {
                    _state.processed = results.Sum(x => x.Processed);
                    _state.total = results.Sum(x => x.Total);
                    _state.ok = results.Sum(x => x.Ok);
                    _state.ng = results.Sum(x => x.Ng);
                    _state.resultCsv = mode == "blue" ? null : MergePositionResultCsv(req.outputRoot, results.Select(x => x.CsvPath));
                    _state.activePositionWorkers = 0;
                    _state.completedPositionWorkers = results.Count;
                    _state.message = "Position 병렬 Simulation 완료 | " + results.Count + "개";
                }
            }
            finally
            {
                lock (_parallelProgressSync)
                {
                    _parallelCoordinatorToken = "";
                    _parallelProgressStates = null;
                    _parallelPositionDisplayNames = null;
                }
                gate.Dispose();
            }
        }

        private void CleanupParallelTemporaryCrops(AgentStartRequest req)
        {
            if (!string.Equals((req.mode ?? "").Trim(), "integrated", StringComparison.OrdinalIgnoreCase) || GetIntegratedOptions(req).keepCropImages) return;
            string temporaryCropRoot = Path.Combine(req.outputRoot, "_VisionQC_BlueCrop_Temp");
            try
            {
                if (Directory.Exists(temporaryCropRoot)) Directory.Delete(temporaryCropRoot, true);
            }
            catch (SysException cleanupError)
            {
                AppendAgentLog("WARN", "병렬 통합 임시 이미지 정리 실패(검사 결과는 유지): " + cleanupError.Message);
            }
        }

        private AgentStartRequest ClonePositionRequest(AgentStartRequest source, AgentPositionRequest position, Dictionary<string, string> assignments)
        {
            AgentStartRequest clone = _json.Deserialize<AgentStartRequest>(_json.Serialize(source));
            clone.positions = clone.positions.Where(item => string.Equals(item.key, position.key, StringComparison.OrdinalIgnoreCase)).ToList();
            string assigned;
            if (assignments != null && assignments.TryGetValue(position.key, out assigned) && assigned != "CPU")
            {
                if (clone.green != null) clone.green.gpuDevices = assigned;
                if (clone.blue != null) clone.blue.gpuDevices = assigned;
            }
            clone.parallelPositions = false;
            clone.autoDistributeGpu = false;
            clone.parallelPositionKey = position.key;
            return clone;
        }

        private static string PositionCropRoot(AgentStartRequest req, bool keepCropImages)
        {
            string root = Path.Combine(req.outputRoot, keepCropImages ? "_VisionQC_Integrated_Images" : "_VisionQC_BlueCrop_Temp");
            if (string.IsNullOrWhiteSpace(req.parallelPositionKey)) return root;
            string safe = req.parallelPositionKey;
            foreach (char invalid in Path.GetInvalidFileNameChars()) safe = safe.Replace(invalid, '_');
            return Path.Combine(root, safe);
        }

        private void OnParallelEngineProgress(string positionKey, string displayName, ProcessProgress progress, Dictionary<string, PositionProgressState> states, object progressSync)
        {
            ProcessProgress aggregate;
            lock (progressSync)
            {
                PositionProgressState state = states[positionKey];
                if (progress.Processed.HasValue) state.Processed = progress.Processed.Value;
                else if (progress.LiveRecord != null) state.Processed++;
                if (progress.Total.HasValue) state.Total = progress.Total.Value;
                if (progress.OkCount.HasValue) state.Ok = progress.OkCount.Value;
                if (progress.NgCount.HasValue) state.Ng = progress.NgCount.Value;
                aggregate = new ProcessProgress
                {
                    Message = string.IsNullOrWhiteSpace(progress.Message) ? null : "[" + displayName + "] " + progress.Message,
                    Processed = states.Values.Sum(x => x.Processed), Total = states.Values.Sum(x => x.Total),
                    OkCount = states.Values.Sum(x => x.Ok), NgCount = states.Values.Sum(x => x.Ng),
                    CurrentFile = progress.CurrentFile, LiveRecord = progress.LiveRecord
                };
            }
            OnEngineProgress(aggregate);
        }

        private static string MergePositionResultCsv(string outputRoot, IEnumerable<string> paths)
        {
            var inputs = paths.Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path)).ToList();
            if (inputs.Count == 0) return "";
            if (inputs.Count == 1) return inputs[0];
            string output = Path.Combine(outputRoot, "results_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_parallel.csv");
            using (var writer = new StreamWriter(output, false, new UTF8Encoding(true)))
            {
                bool headerWritten = false;
                foreach (string input in inputs)
                {
                    using (var reader = new StreamReader(input, true))
                    {
                        string header = reader.ReadLine();
                        if (!headerWritten && header != null) { writer.WriteLine(header); headerWritten = true; }
                        string line;
                        while ((line = reader.ReadLine()) != null) writer.WriteLine(line);
                    }
                }
            }
            return output;
        }

        private void StartSimulationHistory(AgentStartRequest request)
        {
            try
            {
                lock (_historyWriteSync)
                {
                    _lastSimulationRunId = "";
                    lock (_sync) _state.simulationRunId = "";
                    if (_simulationHistorySession != null) _historyStore.Complete(_simulationHistorySession, "replaced", "새 Simulation 실행으로 교체됨");
                    _simulationHistorySession = _historyStore.Start(new SqliteRunStore.RunStoreStart
                    {
                        SourceType = "simulation",
                        Mode = request.mode ?? "green",
                        SourceName = "VPDL Simulation",
                        AgentVersion = Program.AgentVersion,
                        WebVersion = request.webVersion ?? "",
                        OutputRoot = request.outputRoot ?? "",
                        ConfigJson = _json.Serialize(request),
                        NamingProfile = request.namingProfile,
                        NamingProfileJson = _json.Serialize(request.namingProfile ?? new NamingProfile()),
                        WorkspaceType = (request.mode ?? "green").Trim().ToLowerInvariant(),
                        WorkspacesByPosition = BuildHistoryWorkspaceMap(request)
                    });
                    _lastSimulationRunId = _simulationHistorySession.RunId;
                    lock (_sync) _state.simulationRunId = _lastSimulationRunId;
                    _simulationHistoryWriteFailed = false;
                }
            }
            catch (SysException ex)
            {
                _simulationHistorySession = null;
                AppendAgentLog("WARN", "SQLite Simulation 이력 시작 실패(검사는 계속 진행): " + ex.Message);
            }
        }

        private SqliteRunStore.SimulationResultPage ReadSimulationResults(string body)
        {
            var data = DeserializeDictionary(body);
            string runId = GetString(data, "runId", "").Trim();
            string expected;
            lock (_historyWriteSync) expected = _lastSimulationRunId;
            if (string.IsNullOrWhiteSpace(runId) || !string.Equals(runId, expected, StringComparison.OrdinalIgnoreCase))
                return new SqliteRunStore.SimulationResultPage { ok = false, runId = runId, error = "현재 Simulation 실행 ID가 아닙니다." };
            return _historyStore.ReadSimulationResultPage(runId, GetLong(data, "afterImageId", 0), GetInt(data, "pageSize", 500));
        }

        private static Dictionary<string, SqliteRunStore.HistoryWorkspaceValue> BuildHistoryWorkspaceMap(AgentStartRequest request)
        {
            var result = new Dictionary<string, SqliteRunStore.HistoryWorkspaceValue>(StringComparer.OrdinalIgnoreCase);
            string mode = (request == null ? "green" : request.mode ?? "green").Trim().ToLowerInvariant();
            foreach (AgentPositionRequest position in request == null || request.positions == null ? new List<AgentPositionRequest>() : request.positions)
            {
                if (position == null || !position.enabled) continue;
                string greenPath = mode == "blue" ? "" : FirstNonEmpty(position.greenWorkspacePath, position.workspacePath);
                string bluePath = mode == "green" ? "" : FirstNonEmpty(position.blueWorkspacePath, position.workspacePath);
                string greenName = string.IsNullOrWhiteSpace(greenPath) ? "" : Path.GetFileName(greenPath);
                string blueName = string.IsNullOrWhiteSpace(bluePath) ? "" : Path.GetFileName(bluePath);
                string name = mode == "integrated"
                    ? string.Join(" + ", new[] { greenName, blueName }.Where(x => !string.IsNullOrWhiteSpace(x)))
                    : FirstNonEmpty(mode == "blue" ? blueName : greenName, greenName, blueName);
                string key = mode + "|G:" + NormalizeHistoryWorkspacePath(greenPath) + "|B:" + NormalizeHistoryWorkspacePath(bluePath);
                var value = new SqliteRunStore.HistoryWorkspaceValue { Type = mode, Name = name, Key = key };
                string displayName = FirstNonEmpty(position.displayName, position.key);
                if (!string.IsNullOrWhiteSpace(displayName)) result[displayName] = value;
                if (!string.IsNullOrWhiteSpace(position.key)) result[position.key] = value;
            }
            return result;
        }

        private static string NormalizeHistoryWorkspacePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "";
            try { return Path.GetFullPath(path.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToLowerInvariant(); }
            catch { return path.Trim().ToLowerInvariant(); }
        }

        private void AppendSimulationHistory(LiveAnalysisRecord record)
        {
            if (record == null) return;
            try
            {
                lock (_historyWriteSync)
                {
                    if (_simulationHistorySession == null || _simulationHistoryWriteFailed) return;
                    _historyStore.AppendLiveRecord(_simulationHistorySession, record);
                }
            }
            catch (SysException ex)
            {
                _simulationHistoryWriteFailed = true;
                AppendAgentLog("WARN", "SQLite Simulation 이력 기록 실패(검사는 계속 진행): " + ex.Message);
            }
        }

        private void CompleteSimulationHistory(string status, string message)
        {
            try
            {
                lock (_historyWriteSync)
                {
                    if (_simulationHistorySession == null) return;
                    _historyStore.Complete(_simulationHistorySession, status, message ?? "");
                    _simulationHistorySession = null;
                }
            }
            catch (SysException ex)
            {
                AppendAgentLog("WARN", "SQLite Simulation 이력 마감 실패: " + ex.Message);
            }
        }

        private void OnEngineProgress(ProcessProgress p)
        {
            List<LiveAnalysisRecord> batch = null;
            bool progressBoundary = false;
            string logMessage = null;

            lock (_sync)
            {
                if (p.Total.HasValue) _state.total = p.Total.Value;
                if (p.OkCount.HasValue) _state.ok = p.OkCount.Value;
                if (p.NgCount.HasValue) _state.ng = p.NgCount.Value;
                if (!string.IsNullOrWhiteSpace(p.CurrentFile)) _state.current = Path.GetFileName(p.CurrentFile);

                if (p.LiveRecord != null)
                {
                    _liveRecordCount++;
                    // Integrated Streaming은 LiveRecord 이벤트에 Processed가 없으므로
                    // 실제 상세 결과 수를 처리 수로 사용한다.
                    _state.processed = p.Processed.HasValue ? Math.Max(_state.processed, p.Processed.Value) : Math.Max(_state.processed, _liveRecordCount);
                    _liveBuffer.Add(p.LiveRecord);
                    if (_liveBuffer.Count >= Math.Max(1, _liveBatchSize))
                    {
                        batch = _liveBuffer.ToList();
                        _liveBuffer.Clear();
                        progressBoundary = true;
                    }
                }
                else if (p.Processed.HasValue)
                {
                    _state.processed = p.Processed.Value;
                    int n = Math.Max(1, _liveBatchSize);
                    if ((_state.processed % n) == 0 || (_state.total > 0 && _state.processed >= _state.total))
                        progressBoundary = true;
                }

                if (!string.IsNullOrWhiteSpace(p.Message))
                {
                    _state.message = p.Message;
                    var m = Regex.Match(p.Message, @"OK\s*=\s*(\d+)\s*,?\s*NG\s*=\s*(\d+)", RegexOptions.IgnoreCase);
                    if (m.Success && !p.OkCount.HasValue && !p.NgCount.HasValue)
                    {
                        _state.ok = int.Parse(m.Groups[1].Value);
                        _state.ng = int.Parse(m.Groups[2].Value);
                    }
                    // 처리 번호가 있는 진행 메시지는 아래 PROGRESS 한 줄로만 기록한다.
                    // Processor가 LiveRecord와 PrintEvery 메시지를 연달아 보내도 INFO 중복을 만들지 않는다.
                    if (!p.Processed.HasValue && p.LiveRecord == null) logMessage = p.Message;
                }
            }

            if (p.LiveRecord != null) AppendSimulationHistory(p.LiveRecord);
            if (batch != null && batch.Count > 0)
            {
                var snap = Snapshot();
                // analysis와 progress를 같은 Batch 경계에서 강제로 보내므로
                // Batch=1도 매 이미지마다, Batch=5면 5/10/15... 기준으로 Web이 갱신된다.
                BroadcastObject("analysis", new { records = batch, state = snap, processed = snap.processed, total = snap.total, batchSize = _liveBatchSize }, true);
            }
            if (progressBoundary)
            {
                var snap = Snapshot();
                bool shouldSend;
                lock (_sync)
                {
                    shouldSend = snap.processed != _lastProgressValue;
                    if (shouldSend) _lastProgressValue = snap.processed;
                }
                if (shouldSend)
                {
                    Broadcast("progress", snap, true);
                    AppendAgentLog("PROGRESS", string.Format("{0}/{1} ({2:0.00}%) | OK={3}, NG={4} | {5:0.00} img/s | ETA {6}",
                        snap.processed, snap.total, snap.total > 0 ? snap.processed * 100.0 / snap.total : 0.0,
                        snap.ok, snap.ng, snap.imagesPerSecond, FormatEta(snap.etaSeconds)));
                }
            }
            else if (p.LiveRecord == null && !p.Processed.HasValue && !string.IsNullOrWhiteSpace(p.Message))
                Broadcast("progress", Snapshot(), true);

            if (!string.IsNullOrWhiteSpace(logMessage))
            {
                string level = "INFO";
                if (logMessage.StartsWith("[WARN]", StringComparison.OrdinalIgnoreCase))
                {
                    level = "WARN";
                    logMessage = logMessage.Substring(6).Trim();
                }
                else if (logMessage.StartsWith("[ERROR]", StringComparison.OrdinalIgnoreCase))
                {
                    level = "ERROR";
                    logMessage = logMessage.Substring(7).Trim();
                }
                else if (logMessage.StartsWith("[INFO]", StringComparison.OrdinalIgnoreCase))
                    logMessage = logMessage.Substring(6).Trim();
                AppendAgentLog(level, logMessage);
            }
            ForwardParallelProgress(p);
        }

        private void FlushLiveBatch()
        {
            List<LiveAnalysisRecord> batch = null;
            lock (_sync)
            {
                if (_liveBuffer.Count > 0)
                {
                    batch = _liveBuffer.ToList();
                    _liveBuffer.Clear();
                }
            }
            if (batch != null && batch.Count > 0)
            {
                var snap = Snapshot();
                BroadcastObject("analysis", new { records = batch, state = snap, processed = snap.processed, total = snap.total, batchSize = _liveBatchSize, finalBatch = true }, true);
                bool shouldSend;
                lock (_sync)
                {
                    shouldSend = snap.processed != _lastProgressValue;
                    if (shouldSend) _lastProgressValue = snap.processed;
                }
                if (shouldSend) Broadcast("progress", snap, true);
            }
        }

        private BlueCropConfig BuildBlueConfig(AgentStartRequest req, string outputRoot, bool integrated)
        {
            AgentBlueOptions opt = GetBlueOptions(req);
            AgentIntegratedOptions iopt = GetIntegratedOptions(req);
            var cfg = new BlueCropConfig
            {
                OutputRoot = outputRoot,
                UseGpu = opt.useGpu,
                GpuDevices = ParseGpuList(opt.gpuDevices, opt.useGpu),
                CropWidth = Math.Max(1, opt.cropWidth <= 0 ? 2448 : opt.cropWidth),
                CropHeight = Math.Max(1, opt.cropHeight <= 0 ? 2048 : opt.cropHeight),
                ExpectedXMin = opt.expectedXMin,
                ExpectedXMax = opt.expectedXMax,
                MaxYDiff = opt.maxYDiff,
                KeepSubfolders = opt.keepSubfolders,
                SaveAsJpeg = opt.saveAsJpeg,
                JpegQuality = Clamp(opt.jpegQuality, 1, 100, 80),
                SkipExisting = opt.skipExisting,
                PrintEvery = Math.Max(1, opt.printEvery <= 0 ? 100 : opt.printEvery)
            };
            cfg.Slots = EnabledPositions(req).Select(p => new BlueWorkspaceSlotConfig
            {
                Key = p.key,
                DisplayName = p.displayName,
                Enabled = true,
                RuntimeWorkspacePath = FirstNonEmpty(p.blueWorkspacePath, p.workspacePath),
                ImageRoots = integrated && iopt.keywordMode
                    ? GetIntegratedKeywordImageRoots(iopt)
                    : GetBlueImageRoots(p),
                ImageRoot = integrated && iopt.keywordMode
                    ? FirstNonEmpty(GetIntegratedKeywordImageRoots(iopt).ToArray())
                    : FirstNonEmpty(GetBlueImageRoots(p).ToArray()),
                StreamName = FirstNonEmpty(p.blueStreamName, p.streamName, "기본값"),
                BlueToolName = FirstNonEmpty(p.blueToolName, "Locate"),
                Keyword = integrated ? FirstNonEmpty(p.integratedKeyword, p.keyword) : ""
            }).ToList();

            cfg.ToolFallbacks = new List<BlueToolFallbackConfig>();
            var incoming = opt.fallbacks ?? new List<AgentBlueFallbackRequest>();
            foreach (var slot in cfg.Slots)
            {
                var match = incoming.FirstOrDefault(x => x != null &&
                    string.Equals(x.slotKey, slot.Key, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(FirstNonEmpty(x.toolName, slot.BlueToolName), slot.BlueToolName, StringComparison.OrdinalIgnoreCase));
                cfg.ToolFallbacks.Add(new BlueToolFallbackConfig
                {
                    SlotKey = slot.Key,
                    DisplayName = slot.DisplayName,
                    ToolName = slot.BlueToolName,
                    FallbackShiftX = match == null ? 0 : match.fallbackShiftX,
                    FallbackShiftY = match == null ? 200 : match.fallbackShiftY,
                    PreviewRoiX = match == null ? 400 : match.previewRoiX,
                    PreviewRoiY = match == null ? 570 : match.previewRoiY,
                    PreviewRoiW = match == null ? 1658 : match.previewRoiW,
                    PreviewRoiH = match == null ? 589 : match.previewRoiH,
                    SampleImagePath = match == null ? "" : (match.sampleImagePath ?? "")
                });
            }
            return cfg;
        }

        private AppConfig BuildGreenConfig(AgentStartRequest req, string outputRoot, string cropRoot, bool integrated)
        {
            AgentGreenOptions opt = GetGreenOptions(req);
            AgentIntegratedOptions iopt = GetIntegratedOptions(req);
            var judgementList = BuildJudgements(opt);
            var tools = BuildTools(opt);
            string workspaceType = (req.mode ?? (integrated ? "integrated" : "green")).Trim().ToLowerInvariant();
            var historyWorkspaces = BuildHistoryWorkspaceMap(req);
            var enabledKeysForTools = EnabledPositions(req).Select(p => p.key).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            foreach (var tool in tools)
            {
                tool.PositionKeys = enabledKeysForTools.ToList();
                tool.UseCaTop = tool.UseCaBot = tool.UseAnTop = tool.UseAnBot = true;
            }
            var cfg = new AppConfig
            {
                WorkspaceType = workspaceType,
                OutputRoot = outputRoot,
                CellIdCsvPath = integrated ? (iopt.cellIdCsvPath ?? "") : (opt.cellIdCsvPath ?? ""),
                KeywordMode = integrated ? false : opt.keywordMode,
                KeywordInputRoot = integrated ? "" : FirstNonEmpty(GetGreenKeywordImageRoots(opt).ToArray()),
                NamingProfile = req.namingProfile,
                DetailedDiagnostics = !integrated && opt.detailedDiagnostics,
                DisableOptimizedGpuMemory = !integrated && opt.disableOptimizedGpuMemory,
                UseGpu = opt.useGpu,
                GpuDevices = ParseGpuList(opt.gpuDevices, opt.useGpu),
                JpegQuality = Clamp(opt.jpegQuality, 1, 100, 80),
                HeatmapAlpha = Math.Max(0f, Math.Min(1f, opt.heatmapAlpha / 100f)),
                HeatmapAlphaCut = Convert.ToByte(Math.Max(0, Math.Min(255, opt.heatmapAlphaCut))),
                // Heatmap Overlay는 Green 원본 검사 결과에만 저장한다. Integrated는 Blue Crop 파이프라인과 분리한다.
                HeatmapImageSave = !integrated && opt.heatmapImageSave,
                KeepSubfolders = opt.keepSubfolders,
                ResultFileSuffix = req.parallelPositionKey,
                ForceJetWhenGrayscale = opt.forceJet,
                PrintEvery = Math.Max(1, opt.printEvery <= 0 ? 100 : opt.printEvery),
                Tools = tools,
                Judgements = judgementList
            };
            cfg.WorkspaceSlots = EnabledPositions(req).Select(p =>
            {
                SqliteRunStore.HistoryWorkspaceValue historyWorkspace = null;
                historyWorkspaces.TryGetValue(FirstNonEmpty(p.displayName, p.key), out historyWorkspace);
                return new WorkspaceSlotConfig
                {
                    Key = p.key,
                    DisplayName = p.displayName,
                    Enabled = true,
                    WorkspacePath = FirstNonEmpty(p.greenWorkspacePath, p.workspacePath),
                    HistoryWorkspaceName = historyWorkspace == null ? "" : historyWorkspace.Name,
                    HistoryWorkspaceKey = historyWorkspace == null ? "" : historyWorkspace.Key,
                    InputRoots = integrated
                        ? NormalizeImageRoots(null, Path.Combine(cropRoot, p.displayName))
                        : (opt.keywordMode ? GetGreenKeywordImageRoots(opt) : GetGreenImageRoots(p)),
                    InputRoot = integrated
                        ? Path.Combine(cropRoot, p.displayName)
                        : (opt.keywordMode ? FirstNonEmpty(GetGreenKeywordImageRoots(opt).ToArray()) : FirstNonEmpty(GetGreenImageRoots(p).ToArray())),
                    StreamName = FirstNonEmpty(p.greenStreamName, p.streamName, "기본값"),
                    // Keyword가 비어 있으면 공통 입력 Root의 모든 이미지를 이 Position으로 검사한다.
                    // Keyword를 입력한 Position만 해당 문자열이 포함된 파일로 범위를 좁힌다.
                    Keyword = integrated ? "" : FirstNonEmpty(p.greenKeyword, p.keyword),
                    Electrode = p.key != null && p.key.StartsWith("CA", StringComparison.OrdinalIgnoreCase) ? "CA" : "AN",
                    Side = p.key != null && p.key.EndsWith("TOP", StringComparison.OrdinalIgnoreCase) ? "TOP" : "BOT"
                };
            }).ToList();
            return cfg;
        }

        private List<JudgementConfig> BuildJudgements(AgentGreenOptions opt)
        {
            var source = opt.judgements ?? new List<AgentJudgementRequest>();
            var list = source.Where(x => x != null && !string.IsNullOrWhiteSpace(x.name))
                .Select(x => new JudgementConfig { Priority = x.priority <= 0 ? 1 : x.priority, Name = x.name.Trim() })
                .OrderBy(x => x.Priority).ToList();
            if (list.Count == 0) list = JudgementConfig.CreateDefault();
            if (!list.Any(x => string.Equals(x.Name, "ERROR", StringComparison.OrdinalIgnoreCase)))
                list.Add(new JudgementConfig { Priority = list.Max(x => x.Priority) + 1, Name = "ERROR" });
            return list.OrderBy(x => x.Priority).ToList();
        }

        private List<ToolRoiConfig> BuildTools(AgentGreenOptions opt)
        {
            var source = opt.tools ?? new List<AgentToolRequest>();
            if (source.Count == 0) return ToolRoiConfig.CreateDefault();
            var defaults = ToolRoiConfig.CreateDefault();
            var list = new List<ToolRoiConfig>();
            foreach (var item in source)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.toolName)) continue;
                var match = defaults.FirstOrDefault(x => string.Equals(x.ToolName, item.toolName.Trim(), StringComparison.OrdinalIgnoreCase));
                list.Add(new ToolRoiConfig
                {
                    ToolName = item.toolName.Trim(),
                    Roi = match == null ? System.Drawing.Rectangle.Empty : match.Roi,
                    NgScoreThreshold = item.threshold,
                    JudgementName = FirstNonEmpty(item.judgement, "Scrap")
                });
            }
            return list.Count == 0 ? ToolRoiConfig.CreateDefault() : list;
        }

        private AgentGreenOptions GetGreenOptions(AgentStartRequest req)
        {
            if (req.green != null) return req.green;
            return new AgentGreenOptions
            {
                useGpu = req.useGpu, gpuDevices = FirstNonEmpty(req.gpuDevices, "0"), jpegQuality = req.jpegQuality <= 0 ? 80 : req.jpegQuality,
                printEvery = req.printEvery <= 0 ? 100 : req.printEvery, keepSubfolders = req.keepSubfolders,
                heatmapImageSave = req.heatmapImageSave, heatmapAlpha = 55, heatmapAlphaCut = 25, forceJet = true,
                tools = new List<AgentToolRequest>(), judgements = new List<AgentJudgementRequest>()
            };
        }

        private AgentBlueOptions GetBlueOptions(AgentStartRequest req)
        {
            if (req.blue != null) return req.blue;
            return new AgentBlueOptions
            {
                useGpu = req.useGpu, gpuDevices = FirstNonEmpty(req.gpuDevices, "0"), keepSubfolders = req.keepSubfolders,
                saveAsJpeg = true, skipExisting = false, jpegQuality = req.jpegQuality <= 0 ? 80 : req.jpegQuality,
                printEvery = req.printEvery <= 0 ? 100 : req.printEvery, cropWidth = 2448, cropHeight = 2048,
                expectedXMin = 1100, expectedXMax = 1500, maxYDiff = 300, fallbacks = new List<AgentBlueFallbackRequest>()
            };
        }

        private AgentIntegratedOptions GetIntegratedOptions(AgentStartRequest req)
        {
            if (req.integrated != null) return req.integrated;
            return new AgentIntegratedOptions { keepCropImages = req.keepCropImages, heatmapImageSave = req.heatmapImageSave };
        }

        private int GetLiveBatchSize(AgentStartRequest req)
        {
            string mode = (req.mode ?? "green").Trim().ToLowerInvariant();
            if (mode == "blue") return Math.Max(1, GetBlueOptions(req).printEvery <= 0 ? 100 : GetBlueOptions(req).printEvery);
            return Math.Max(1, GetGreenOptions(req).printEvery <= 0 ? 100 : GetGreenOptions(req).printEvery);
        }

        private string ValidateRequest(AgentStartRequest req)
        {
            if (req == null) return "Simulation 설정이 없습니다.";
            if (string.IsNullOrWhiteSpace(req.outputRoot)) return "Output Folder를 선택하세요.";
            var positions = EnabledPositions(req).ToList();
            if (positions.Count == 0) return "사용할 Position을 1개 이상 추가하세요.";
            string mode = (req.mode ?? "green").Trim().ToLowerInvariant();
            AgentGreenOptions g = GetGreenOptions(req);
            AgentBlueOptions b = GetBlueOptions(req);
            AgentIntegratedOptions i = GetIntegratedOptions(req);

            List<string> greenKeywordRoots = GetGreenKeywordImageRoots(g);
            List<string> integratedKeywordRoots = GetIntegratedKeywordImageRoots(i);
            if ((mode == "green" && g.keywordMode) && (greenKeywordRoots.Count == 0 || greenKeywordRoots.Any(root => !Directory.Exists(root))))
                return "Green Keyword 입력 폴더를 확인하세요.";
            if ((mode == "integrated" && i.keywordMode) && (integratedKeywordRoots.Count == 0 || integratedKeywordRoots.Any(root => !Directory.Exists(root))))
                return "Integrated Keyword 입력 폴더를 확인하세요.";
            string cellCsv = mode == "integrated" ? i.cellIdCsvPath : g.cellIdCsvPath;
            if (mode != "blue" && !string.IsNullOrWhiteSpace(cellCsv))
            {
                if (!File.Exists(cellCsv)) return "Cell ID CSV 파일을 확인하세요.";
                if (GreenOverlayProcessor.CountCellIdFilterForValidation(cellCsv) <= 0)
                    return "Cell ID CSV를 선택했지만 읽을 수 있는 Cell ID가 0개입니다.";
            }
            if (mode != "blue")
            {
                string toolValidation = ValidateGreenToolSettings(g, positions);
                if (!string.IsNullOrEmpty(toolValidation)) return toolValidation;
            }

            foreach (var p in positions)
            {
                if (mode == "green")
                {
                    string ws = FirstNonEmpty(p.greenWorkspacePath, p.workspacePath);
                    if (string.IsNullOrWhiteSpace(ws) || !File.Exists(ws)) return p.displayName + " Green Workspace를 확인하세요.";
                    if (!g.keywordMode)
                    {
                        List<string> roots = GetGreenImageRoots(p);
                        if (roots.Count == 0 || roots.Any(root => !Directory.Exists(root))) return p.displayName + " Green Image Folder를 확인하세요.";
                    }
                }
                else if (mode == "blue")
                {
                    string ws = FirstNonEmpty(p.blueWorkspacePath, p.workspacePath);
                    List<string> roots = GetBlueImageRoots(p);
                    if (string.IsNullOrWhiteSpace(ws) || !File.Exists(ws)) return p.displayName + " Blue Workspace를 확인하세요.";
                    if (roots.Count == 0 || roots.Any(root => !Directory.Exists(root))) return p.displayName + " Blue Image Folder를 확인하세요.";
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(p.greenWorkspacePath) || !File.Exists(p.greenWorkspacePath)) return p.displayName + " Green Workspace를 확인하세요.";
                    if (string.IsNullOrWhiteSpace(p.blueWorkspacePath) || !File.Exists(p.blueWorkspacePath)) return p.displayName + " Blue Workspace를 확인하세요.";
                    if (!i.keywordMode)
                    {
                        List<string> roots = GetBlueImageRoots(p);
                        if (roots.Count == 0 || roots.Any(root => !Directory.Exists(root))) return p.displayName + " Blue Image Folder를 확인하세요.";
                    }
                }
            }
            return null;
        }

        private string ValidateGreenToolSettings(AgentGreenOptions opt, List<AgentPositionRequest> positions)
        {
            var judgements = (opt.judgements ?? new List<AgentJudgementRequest>())
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.name)).ToList();
            if (judgements.Count == 0) judgements = new List<AgentJudgementRequest>
            {
                new AgentJudgementRequest { priority = 1, name = "Crack" },
                new AgentJudgementRequest { priority = 2, name = "Damage" },
                new AgentJudgementRequest { priority = 3, name = "Scrap" },
                new AgentJudgementRequest { priority = 99, name = "ERROR" }
            };
            if (judgements.GroupBy(x => x.priority).Any(g => g.Count() > 1)) return "Judgement Priority가 중복되었습니다.";
            if (judgements.GroupBy(x => x.name.Trim(), StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1)) return "Judgement Name이 중복되었습니다.";
            var allowed = new HashSet<string>(judgements.Select(x => x.name.Trim()), StringComparer.OrdinalIgnoreCase);

            var tools = (opt.tools ?? new List<AgentToolRequest>()).Where(x => x != null && !string.IsNullOrWhiteSpace(x.toolName)).ToList();
            if (tools.Count == 0) return "Green Tool을 1개 이상 입력하세요.";
            if (tools.GroupBy(x => x.toolName.Trim(), StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
                return "ToolName이 중복되었습니다.";
            foreach (var tool in tools)
            {
                if (string.IsNullOrWhiteSpace(tool.judgement) || !allowed.Contains(tool.judgement.Trim()))
                    return "Tool '" + tool.toolName + "'의 Judgement 설정을 확인하세요.";
            }
            return null;
        }

        private IEnumerable<AgentPositionRequest> EnabledPositions(AgentStartRequest req)
        {
            return (req.positions ?? new List<AgentPositionRequest>()).Where(p => p != null && p.enabled);
        }

        private void Broadcast(string eventName, SimulationState state, bool force)
        {
            if (!force && (DateTime.UtcNow - _lastProgressBroadcast).TotalMilliseconds < 180) return;
            if (eventName == "progress") _lastProgressBroadcast = DateTime.UtcNow;
            BroadcastSerialized(eventName, _json.Serialize(state));
        }

        private void BroadcastObject(string eventName, object data, bool force)
        {
            // 분석 Batch와 로그는 progress throttling과 완전히 분리한다.
            BroadcastSerialized(eventName, _json.Serialize(data));
        }

        private void BroadcastSerialized(string eventName, string payload)
        {
            List<SseClient> clients;
            lock (_sync) clients = _sse.ToList();
            foreach (var client in clients)
            {
                try
                {
                    byte[] bytes = Encoding.UTF8.GetBytes("event: " + eventName + "\ndata: " + payload + "\n\n");
                    lock (client.Sync) client.Stream.Write(bytes, 0, bytes.Length);
                }
                catch
                {
                    lock (_sync) _sse.Remove(client);
                    try { client.Client.Close(); } catch { }
                }
            }
        }

        private void AppendAgentLog(string level, string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            string normalizedLevel = (level ?? "INFO").Trim().ToUpperInvariant();
            string key = normalizedLevel + "|" + message.Trim();
            lock (_sync)
            {
                if (string.Equals(_lastAgentLogKey, key, StringComparison.Ordinal) &&
                    (DateTime.UtcNow - _lastAgentLogUtc).TotalMilliseconds < 1000) return;
                _lastAgentLogKey = key;
                _lastAgentLogUtc = DateTime.UtcNow;
            }
            BroadcastObject("log", new
            {
                time = DateTime.Now.ToString("HH:mm:ss.fff"),
                level = normalizedLevel,
                message = message,
                state = Snapshot()
            }, true);
            if (normalizedLevel != "PROGRESS") AgentDiagnostics.Write(normalizedLevel, message);
        }

        private async Task WriteSse(SseClient client, string eventName, object data)
        {
            string payload = _json.Serialize(data);
            byte[] bytes = Encoding.UTF8.GetBytes("event: " + eventName + "\ndata: " + payload + "\n\n");
            await client.Stream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
        }

        private static string FormatEta(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds <= 0) return "--:--:--";
            var ts = TimeSpan.FromSeconds(seconds);
            if (ts.TotalDays >= 1) return string.Format("{0}d {1:00}:{2:00}:{3:00}", (int)ts.TotalDays, ts.Hours, ts.Minutes, ts.Seconds);
            return string.Format("{0:00}:{1:00}:{2:00}", (int)ts.TotalHours, ts.Minutes, ts.Seconds);
        }

        private SimulationState Snapshot()
        {
            lock (_sync)
            {
                double elapsed = _simulationStartedUtc == DateTime.MinValue ? 0.0 : Math.Max(0.0, (DateTime.UtcNow - _simulationStartedUtc).TotalSeconds);
                double ips = elapsed > 0.05 && _state.processed > 0 ? _state.processed / elapsed : 0.0;
                double eta = ips > 0.0001 && _state.total > _state.processed ? (_state.total - _state.processed) / ips : 0.0;
                return new SimulationState
                {
                    running = _state.running, mode = _state.mode, processed = _state.processed, total = _state.total,
                    ok = _state.ok, ng = _state.ng, current = _state.current, message = _state.message,
                    outputRoot = _state.outputRoot, resultCsv = _state.resultCsv, error = _state.error,
                    elapsedSeconds = elapsed, etaSeconds = eta, imagesPerSecond = ips, batchSize = _liveBatchSize,
                    activePositionWorkers = _state.activePositionWorkers, completedPositionWorkers = _state.completedPositionWorkers,
                    simulationRunId = _state.simulationRunId
                };
            }
        }

        private static SimulationState NewIdleState()
        {
            return new SimulationState { running = false, mode = "", current = "-", message = "Ready" };
        }

        private string DetectVpdlVersion()
        {
            // 현재 Worker가 실제로 바인딩한 VPDL만 상태로 표시한다.
            // 단순히 먼저 발견한 폴더를 표시하면 다른 버전 DLL 혼용 여부를 숨길 수 있다.
            var active = Program.ActiveVpdlInstallation;
            return active == null ? "-" : active.ProductVersion;
        }

        private string DetectGpuName()
        {
            try
            {
                var names = new List<string>();
                using (var searcher = new ManagementObjectSearcher("SELECT Name, DriverVersion FROM Win32_VideoController"))
                using (var results = searcher.Get())
                {
                    foreach (ManagementObject item in results)
                    {
                        string name = Convert.ToString(item["Name"]);
                        AgentDiagnostics.Write("GPU_DRIVER", name + " | Windows driver=" + Convert.ToString(item["DriverVersion"]));
                        if (!string.IsNullOrWhiteSpace(name) && name.IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) >= 0) names.Add(name);
                    }
                }
                return names.Count == 0 ? "-" : string.Join(" / ", names);
            }
            catch { return "-"; }
        }

        private static List<int> DetectGpuDeviceIndices()
        {
            var indices = new List<int>();
            try
            {
                var start = new ProcessStartInfo
                {
                    FileName = "nvidia-smi.exe",
                    Arguments = "--query-gpu=index --format=csv,noheader,nounits",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                using (Process process = Process.Start(start))
                {
                    string output = process.StandardOutput.ReadToEnd();
                    if (!process.WaitForExit(5000)) { try { process.Kill(); } catch { } return indices; }
                    foreach (string line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        int index;
                        if (int.TryParse(line.Trim(), out index) && !indices.Contains(index)) indices.Add(index);
                    }
                }
            }
            catch { }
            indices.Sort();
            return indices;
        }

        private static List<int> ParseGpuList(string text, bool useGpu)
        {
            var list = new List<int>();
            if (!useGpu) return list;
            foreach (string part in (text ?? "").Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int n;
                if (!int.TryParse(part, out n)) throw new SysInvalidOperationException("GPU Devices 형식이 올바르지 않습니다: " + text);
                list.Add(n);
            }
            if (list.Count == 0) list.Add(0);
            return list;
        }

        private static int Clamp(int value, int min, int max, int fallback)
        {
            if (value <= 0) value = fallback;
            return Math.Max(min, Math.Min(max, value));
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (string value in values) if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
            return "";
        }

        private static List<string> GetGreenImageRoots(AgentPositionRequest position)
        {
            return NormalizeImageRoots(position == null ? null : position.greenImageRoots,
                position == null ? "" : position.greenImageRoot,
                position == null ? "" : position.imageRoot);
        }

        private static List<string> GetBlueImageRoots(AgentPositionRequest position)
        {
            return NormalizeImageRoots(position == null ? null : position.blueImageRoots,
                position == null ? "" : position.blueImageRoot,
                position == null ? "" : position.imageRoot);
        }

        private static List<string> GetGreenKeywordImageRoots(AgentGreenOptions options)
        {
            return NormalizeImageRoots(options == null ? null : options.keywordInputRoots,
                options == null ? "" : options.keywordInputRoot);
        }

        private static List<string> GetIntegratedKeywordImageRoots(AgentIntegratedOptions options)
        {
            return NormalizeImageRoots(options == null ? null : options.keywordInputRoots,
                options == null ? "" : options.keywordInputRoot);
        }

        private static List<string> NormalizeImageRoots(IEnumerable<string> roots, params string[] fallbackRoots)
        {
            var result = new List<string>();
            Action<string> add = value =>
            {
                string path = (value ?? "").Trim();
                if (string.IsNullOrWhiteSpace(path) || result.Any(existing => string.Equals(existing, path, StringComparison.OrdinalIgnoreCase))) return;
                result.Add(path);
            };
            if (roots != null) foreach (string root in roots) add(root);
            if (fallbackRoots != null) foreach (string root in fallbackRoots) add(root);
            return result;
        }

        private Dictionary<string, object> DeserializeDictionary(string body)
        {
            try { return _json.Deserialize<Dictionary<string, object>>(body ?? "{}") ?? new Dictionary<string, object>(); }
            catch { return new Dictionary<string, object>(); }
        }

        private static string GetString(Dictionary<string, object> data, string key, string fallback)
        {
            object value; return data != null && data.TryGetValue(key, out value) && value != null ? Convert.ToString(value) : fallback;
        }

        private static bool GetBool(Dictionary<string, object> data, string key, bool fallback)
        {
            object value; if (data == null || !data.TryGetValue(key, out value) || value == null) return fallback;
            bool b; return bool.TryParse(Convert.ToString(value), out b) ? b : fallback;
        }

        private static int GetInt(Dictionary<string, object> data, string key, int fallback)
        {
            object value; if (data == null || !data.TryGetValue(key, out value) || value == null) return fallback;
            int n; return int.TryParse(Convert.ToString(value), out n) ? n : fallback;
        }

        private static long GetLong(Dictionary<string, object> data, string key, long fallback)
        {
            object value; if (data == null || !data.TryGetValue(key, out value) || value == null) return fallback;
            long n; return long.TryParse(Convert.ToString(value), out n) ? n : fallback;
        }

        private static T RunStaDialog<T>(Func<IWin32Window, T> action)
        {
            T result = default(T);
            SysException error = null;
            var thread = new Thread(() =>
            {
                try
                {
                    using (var owner = new Form())
                    {
                        owner.Text = "VisionQC Local Agent";
                        owner.ShowInTaskbar = false;
                        owner.FormBorderStyle = FormBorderStyle.FixedToolWindow;
                        owner.StartPosition = FormStartPosition.CenterScreen;
                        owner.Width = 2;
                        owner.Height = 2;
                        owner.Opacity = 0.01;
                        owner.TopMost = true;
                        owner.Show();
                        owner.BringToFront();
                        owner.Activate();
                        result = action(owner);
                        owner.Close();
                    }
                }
                catch (SysException ex) { error = ex; }
            });
            thread.IsBackground = true;
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (error != null) throw error;
            return result;
        }

        private async Task<HttpRequest> ReadRequest(NetworkStream stream)
        {
            var buffer = new byte[8192];
            var data = new MemoryStream();
            int headerEnd = -1;
            while (headerEnd < 0 && data.Length < 1024 * 1024)
            {
                int read = await stream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
                if (read <= 0) return null;
                data.Write(buffer, 0, read);
                byte[] arr = data.ToArray();
                headerEnd = FindHeaderEnd(arr);
            }
            if (headerEnd < 0) return null;
            byte[] all = data.ToArray();
            string head = Encoding.UTF8.GetString(all, 0, headerEnd);
            string[] lines = head.Split(new[] { "\r\n" }, StringSplitOptions.None);
            string[] first = lines[0].Split(' ');
            var req = new HttpRequest { Method = first[0].ToUpperInvariant(), Path = first.Length > 1 ? first[1].Split('?')[0] : "/" };
            for (int i = 1; i < lines.Length; i++)
            {
                int colon = lines[i].IndexOf(':');
                if (colon > 0) req.Headers[lines[i].Substring(0, colon).Trim().ToLowerInvariant()] = lines[i].Substring(colon + 1).Trim();
            }
            int contentLength = 0;
            if (req.Headers.ContainsKey("content-length")) int.TryParse(req.Headers["content-length"], out contentLength);
            int bodyStart = headerEnd + 4;
            var body = new MemoryStream();
            if (all.Length > bodyStart) body.Write(all, bodyStart, all.Length - bodyStart);
            while (body.Length < contentLength)
            {
                int read = await stream.ReadAsync(buffer, 0, Math.Min(buffer.Length, contentLength - (int)body.Length)).ConfigureAwait(false);
                if (read <= 0) break;
                body.Write(buffer, 0, read);
            }
            req.Body = Encoding.UTF8.GetString(body.ToArray());
            return req;
        }

        private static int FindHeaderEnd(byte[] data)
        {
            for (int i = 0; i <= data.Length - 4; i++)
                if (data[i] == 13 && data[i + 1] == 10 && data[i + 2] == 13 && data[i + 3] == 10) return i;
            return -1;
        }

        private async Task WriteJson(NetworkStream stream, int status, object data, string origin)
        {
            await WriteResponse(stream, status, "application/json; charset=utf-8", _json.Serialize(data), origin).ConfigureAwait(false);
        }

        private async Task OpenOfflinePage()
        {
            // 리스너가 완전히 준비된 뒤 기본 브라우저에서 로컬 UI를 연다.
            await Task.Delay(350).ConfigureAwait(false);
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "http://127.0.0.1:" + _port + "/",
                    UseShellExecute = true
                });
            }
            catch { }
        }

        private async Task WriteOfflineWebAsset(NetworkStream stream, string requestPath, string origin)
        {
            const string notFound = "<!doctype html><meta charset=\"utf-8\"><title>VisionQC Offline</title><p>오프라인 UI 파일을 찾을 수 없습니다. VisionQC_Agent_Installer를 다시 실행해 주세요.</p>";
            try
            {
                string relative = Uri.UnescapeDataString(requestPath ?? "/");
                if (relative == "/" || string.Equals(relative, "/offline", StringComparison.OrdinalIgnoreCase)) relative = "/index.html";
                relative = relative.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
                if (string.IsNullOrWhiteSpace(relative)) relative = "index.html";

                string root = Path.GetFullPath(Path.Combine(Program.AgentHomeDirectory, "Web"));
                string path = Path.GetFullPath(Path.Combine(root, relative));
                if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && !string.Equals(path, root, StringComparison.OrdinalIgnoreCase))
                {
                    await WriteResponse(stream, 404, "text/html; charset=utf-8", notFound, origin).ConfigureAwait(false);
                    return;
                }
                if (!File.Exists(path))
                {
                    await WriteResponse(stream, 404, "text/html; charset=utf-8", notFound, origin).ConfigureAwait(false);
                    return;
                }
                await WriteBytesResponse(stream, 200, OfflineContentType(path), File.ReadAllBytes(path), origin).ConfigureAwait(false);
            }
            catch
            {
                await WriteResponse(stream, 500, "text/html; charset=utf-8", notFound, origin).ConfigureAwait(false);
            }
        }

        private static string OfflineContentType(string path)
        {
            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".html": return "text/html; charset=utf-8";
                case ".js": return "application/javascript; charset=utf-8";
                case ".css": return "text/css; charset=utf-8";
                case ".json": return "application/json; charset=utf-8";
                case ".svg": return "image/svg+xml";
                case ".png": return "image/png";
                case ".jpg": case ".jpeg": return "image/jpeg";
                case ".ico": return "image/x-icon";
                case ".woff2": return "font/woff2";
                default: return "application/octet-stream";
            }
        }

        private async Task WriteResponse(NetworkStream stream, int status, string contentType, string body, string origin)
        {
            await WriteBytesResponse(stream, status, contentType, Encoding.UTF8.GetBytes(body ?? ""), origin).ConfigureAwait(false);
        }

        private async Task WriteBytesResponse(NetworkStream stream, int status, string contentType, byte[] bytes, string origin)
        {
            bytes = bytes ?? new byte[0];
            string reason = status == 200 ? "OK" : status == 204 ? "No Content" : status == 404 ? "Not Found" : status >= 500 ? "Internal Server Error" : "Error";
            string headers = "HTTP/1.1 " + status + " " + reason + "\r\n" +
                "Content-Type: " + contentType + "\r\n" +
                "Content-Length: " + bytes.Length + "\r\n" +
                CorsHeaders(origin) +
                "Connection: close\r\n\r\n";
            byte[] head = Encoding.ASCII.GetBytes(headers);
            await stream.WriteAsync(head, 0, head.Length).ConfigureAwait(false);
            if (bytes.Length > 0) await stream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
        }

        private async Task WriteSseHeaders(NetworkStream stream, string origin)
        {
            string headers = "HTTP/1.1 200 OK\r\n" +
                "Content-Type: text/event-stream; charset=utf-8\r\n" +
                "Cache-Control: no-cache\r\n" +
                CorsHeaders(origin) +
                "Connection: keep-alive\r\n\r\n";
            byte[] head = Encoding.ASCII.GetBytes(headers);
            await stream.WriteAsync(head, 0, head.Length).ConfigureAwait(false);
        }

        private string CorsHeaders(string origin)
        {
            string allowed = IsAllowedOrigin(origin) ? origin : "https://chabalgo.github.io";
            return "Access-Control-Allow-Origin: " + allowed + "\r\n" +
                   "Vary: Origin\r\n" +
                   "Access-Control-Allow-Methods: GET, POST, OPTIONS\r\n" +
                   "Access-Control-Allow-Headers: Content-Type\r\n" +
                   "Access-Control-Allow-Private-Network: true\r\n";
        }

        private static bool IsExpectedClientDisconnect(SysException ex)
        {
            if (ex is SocketException) return true;
            if (ex is IOException && ex.InnerException is SocketException) return true;
            var message = (ex.Message ?? string.Empty).ToLowerInvariant();
            return message.Contains("forcibly closed")
                || message.Contains("connection was aborted")
                || message.Contains("연결은 사용자의 호스트 시스템")
                || message.Contains("전송 연결");
        }

        private static bool IsAllowedOrigin(string origin)
        {
            if (string.IsNullOrWhiteSpace(origin)) return true;
            if (string.Equals(origin, "https://chabalgo.github.io", StringComparison.OrdinalIgnoreCase)) return true;
            if (origin.StartsWith("http://127.0.0.1:", StringComparison.OrdinalIgnoreCase)) return true;
            if (origin.StartsWith("http://localhost:", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public void Dispose()
        {
            try { _picker.Dispose(); } catch { }
            try { CompleteSimulationHistory("interrupted", "Agent 종료"); } catch { }
            try { _history.Dispose(); } catch { }
            try { _historyStore.Dispose(); } catch { }
            try { _simulationCts?.Cancel(); } catch { }
            try { _serverCts.Cancel(); } catch { }
            try { _listener?.Stop(); } catch { }
            lock (_sync)
            {
                foreach (var c in _sse) try { c.Client.Close(); } catch { }
                _sse.Clear();
            }
            lock (_vpdlSync)
            {
                DisposeInspectionControlLocked();
                DisposePreloadedRuntimeLocked();
                _workspaceInspectionCache.Clear();
            }
        }

        private sealed class WorkspaceInspectionCacheEntry
        {
            public long Length;
            public DateTime LastWriteUtc;
            public WorkspaceInspectionResponse Result;
        }

        private sealed class DirectProgress<T> : IProgress<T>
        {
            private readonly Action<T> _action;
            public DirectProgress(Action<T> action) { _action = action; }
            public void Report(T value) { _action(value); }
        }

        private sealed class HttpRequest
        {
            public string Method;
            public string Path;
            public string Body;
            public readonly Dictionary<string, string> Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class SseClient
        {
            public readonly TcpClient Client;
            public readonly NetworkStream Stream;
            public readonly object Sync = new object();
            public SseClient(TcpClient client, NetworkStream stream) { Client = client; Stream = stream; }
        }
    }
}
