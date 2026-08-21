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
using VpdlGreenHeatmapOverlay;
using SysException = System.Exception;
using SysInvalidOperationException = System.InvalidOperationException;
using VpdlGpuMode = ViDi2.GpuMode;
using LocalRuntime = ViDi2.Runtime.Local;

namespace VisionQC.LocalAgent
{
    internal sealed class AgentServer : IDisposable
    {
        private const int Port = 17891;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        private readonly object _sync = new object();
        private readonly object _vpdlSync = new object();
        private readonly object _pickerSync = new object();
        private readonly object _pickerStateSync = new object();
        private readonly List<SseClient> _sse = new List<SseClient>();
        private readonly Dictionary<string, WorkspaceInspectionCacheEntry> _workspaceInspectionCache = new Dictionary<string, WorkspaceInspectionCacheEntry>(StringComparer.OrdinalIgnoreCase);
        private TcpListener _listener;
        private CancellationTokenSource _serverCts = new CancellationTokenSource();
        private CancellationTokenSource _simulationCts;
        private Task _simulationTask;
        private readonly string _instanceId = Guid.NewGuid().ToString("N");
        private string _licenseStatus = "확인 중";
        private string _runtimeMessage = "Agent 시작 후 Runtime/License 자동 확인 중";
        private readonly string _vpdlVersion;
        private readonly string _gpuName;
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
        private string _preloadedRuntimeSignature = "";
        private string _preloadedRuntimeToken = "";
        private string _preloadedRuntimeMode = "";
        private string _lastAgentLogKey = "";
        private DateTime _lastAgentLogUtc = DateTime.MinValue;
        private string _pickerClientId = "";
        private PickerJob _pickerJob;

        public AgentServer()
        {
            _vpdlVersion = DetectVpdlVersion();
            _gpuName = DetectGpuName();
        }

        public void RunUntilExit()
        {
            _listener = new TcpListener(IPAddress.Loopback, Port);
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
            // Agent는 사용자가 종료할 때까지 유지하고, 시작 직후 License를 확인한다.
            Task.Run(() => RuntimeCheck("{\"useGpu\":true,\"gpuDevices\":\"0\"}"));

            while (!_serverCts.IsCancellationRequested) Thread.Sleep(250);
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
                    case "/api/runtime/check":
                        result = RuntimeCheck(request.Body);
                        break;
                    case "/api/runtime/preload":
                        result = PreloadRuntime(request.Body);
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
                    case "/api/simulation/start":
                        result = StartSimulation(request.Body);
                        break;
                    case "/api/simulation/stop":
                        result = StopSimulation();
                        break;
                    case "/api/simulation/state":
                        result = Snapshot();
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

        private object BuildStatus()
        {
            var state = Snapshot();
            return new
            {
                ok = true,
                instanceId = _instanceId,
                agentVersion = Program.AgentVersion,
                engineVersion = "DL_Simulation v1.13 + VisionQC Workspace Inspect",
                installedVpdlVersion = _vpdlVersion,
                vpdlVersion = (_preloadedRuntimeControl != null || _vpdlReservedForSimulation) ? _vpdlVersion : "-",
                license = _licenseStatus,
                runtimeMessage = _runtimeMessage,
                gpu = _gpuName,
                running = state.running,
                runtimePreloaded = _preloadedRuntimeControl != null,
                runtimePreloadMode = _preloadedRuntimeMode,
                runtimePreloadToken = _preloadedRuntimeToken,
                runtimePreloadSignature = _preloadedRuntimeSignature,
                state = state
            };
        }

        private object RuntimeCheck(string body)
        {
            var req = DeserializeDictionary(body);
            bool useGpu = GetBool(req, "useGpu", true);
            var gpuList = ParseGpuList(GetString(req, "gpuDevices", "0"), useGpu);
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
                try
                {
                    var mode = useGpu ? VpdlGpuMode.SingleDevicePerTool : VpdlGpuMode.NoSupport;
                    EnsureInspectionControl(useGpu, mode, gpuList, false);
                    _licenseStatus = "Runtime OK";
                    _runtimeMessage = "License 확인 완료 · Simulation Runtime 미로드";
                    DisposeInspectionControlLocked();
                    return new { ok = true, license = _licenseStatus, gpu = _gpuName, installedVpdlVersion = _vpdlVersion, vpdlVersion = "-" };
                }
                catch (SysException ex)
                {
                    DisposeInspectionControlLocked();
                    _licenseStatus = "Runtime Error";
                    _runtimeMessage = ex.Message;
                    return new { ok = false, license = _licenseStatus, error = ex.Message, gpu = _gpuName, installedVpdlVersion = _vpdlVersion, vpdlVersion = "-" };
                }
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
                    var gpuMode = useGpu ? VpdlGpuMode.SingleDevicePerTool : VpdlGpuMode.NoSupport;
                    var gpuList = ParseGpuList(gpuDevices, useGpu);
                    control = new LocalRuntime.Control(gpuMode, gpuList);

                    var response = new RuntimePreloadResponse { ok = true, mode = mode, installedVpdlVersion = _vpdlVersion, vpdlVersion = _vpdlVersion };
                    var positions = EnabledPositions(req).ToList();
                    int total = positions.Count * (mode == "integrated" ? 2 : 1);
                    int completed = 0;
                    AppendAgentLog("INFO", "Runtime File Load 시작 | Mode=" + mode + " | Workspace " + total);

                    foreach (var position in positions)
                    {
                        if (mode != "blue")
                        {
                            string path = FirstNonEmpty(position.greenWorkspacePath, position.workspacePath);
                            response.items.Add(LoadPreloadedWorkspace(control, position, "green", "ws_" + position.key, path));
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

                    _preloadedRuntimeControl = control;
                    control = null;
                    _preloadedRuntimeSignature = signature;
                    _preloadedRuntimeToken = Guid.NewGuid().ToString("N");
                    _preloadedRuntimeMode = mode;
                    _licenseStatus = "Runtime Ready";
                    _runtimeMessage = "Runtime File Load 완료 · Simulation 시작 대기";
                    response.token = _preloadedRuntimeToken;
                    response.signature = _preloadedRuntimeSignature;
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
                    DisposePreloadedRuntimeLocked();
                    sw.Stop();
                    _licenseStatus = "Runtime Error";
                    _runtimeMessage = ex.Message;
                    AppendAgentLog("ERROR", "Runtime File Load 실패: " + ex.Message);
                    return new RuntimePreloadResponse { ok = false, mode = mode, error = ex.Message, elapsedMs = sw.ElapsedMilliseconds };
                }
            }
        }

        private RuntimePreloadItem LoadPreloadedWorkspace(LocalRuntime.Control control, AgentPositionRequest position, string kind, string workspaceName, string path)
        {
            AppendAgentLog("INFO", position.displayName + " " + kind.ToUpperInvariant() + " Runtime 로드 중: " + Path.GetFileName(path));
            ViDi2.Runtime.IWorkspace workspace = control.Workspaces.Add(workspaceName, path);
            // VPDL Workspaces 컬렉션은 버전에 따라 문자열 indexer가 동일하게 동작하지 않습니다.
            // Add가 반환한 실제 객체를 Control과 이름으로 직접 보관해 Simulation에서 재사용합니다.
            RuntimeWorkspaceRegistry.Register(control, workspaceName, workspace);
            WorkspaceInspectionResponse info = BuildWorkspaceInspectionResult(workspace, path, "RuntimePreload");
            return new RuntimePreloadItem
            {
                positionKey = position.key,
                displayName = position.displayName,
                kind = kind,
                info = info
            };
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
            foreach (var p in EnabledPositions(req).OrderBy(x => x.key, StringComparer.OrdinalIgnoreCase))
            {
                sb.Append('|').Append(p.key ?? "");
                if (mode != "blue") sb.Append("|G:").Append(NormalizeRuntimePath(FirstNonEmpty(p.greenWorkspacePath, p.workspacePath)));
                if (mode != "green") sb.Append("|B:").Append(NormalizeRuntimePath(FirstNonEmpty(p.blueWorkspacePath, p.workspacePath)));
            }
            return sb.ToString();
        }

        private static string NormalizeRuntimePath(string path)
        {
            try { return Path.GetFullPath(path ?? "").TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToUpperInvariant(); }
            catch { return (path ?? "").Trim().ToUpperInvariant(); }
        }

        private void DisposePreloadedRuntimeLocked()
        {
            try
            {
                if (_preloadedRuntimeControl != null)
                {
                    RuntimeWorkspaceRegistry.Remove(_preloadedRuntimeControl);
                    _preloadedRuntimeControl.Dispose();
                }
            }
            catch { }
            _preloadedRuntimeControl = null;
            _preloadedRuntimeSignature = "";
            _preloadedRuntimeToken = "";
            _preloadedRuntimeMode = "";
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

        private object StartPicker(string body)
        {
            var data = DeserializeDictionary(body);
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
            lock (_pickerStateSync)
            {
                if (_pickerJob != null)
                {
                    bool sameRequest = string.Equals(_pickerJob.ClientId, clientId, StringComparison.Ordinal) &&
                        string.Equals(_pickerJob.RequestId, requestId, StringComparison.Ordinal);
                    bool sameClient = string.Equals(_pickerJob.ClientId, clientId, StringComparison.Ordinal);
                    if (sameRequest) return PickerJobResponse(_pickerJob);
                    bool expiredCompleted = _pickerJob.Completed && _pickerJob.CompletedUtc != DateTime.MinValue &&
                        DateTime.UtcNow - _pickerJob.CompletedUtc > TimeSpan.FromSeconds(30);
                    bool replaceable = _pickerJob.Completed && (_pickerJob.Delivered || sameClient || expiredCompleted);
                    if (!replaceable)
                    {
                        bool recoverable = sameClient;
                        return new
                        {
                            ok = false,
                            pending = false,
                            busy = true,
                            recoverable = recoverable,
                            requestId = _pickerJob.RequestId,
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
                _pickerJob = job;
            }

            // HTTP 응답은 즉시 반환하고 실제 Windows Dialog는 별도 STA 작업에서 실행합니다.
            // 따라서 사용자가 선택하는 동안 브라우저 요청이나 Agent 소켓을 붙잡지 않습니다.
            NativeShellPicker.PrepareDialogRequest();
            _ = Task.Run(() => RunPickerJob(job));
            return PickerJobResponse(job);
        }

        private object PickerStatus(string body)
        {
            var data = DeserializeDictionary(body);
            string clientId = GetString(data, "clientId", "");
            string requestId = GetString(data, "requestId", "");
            lock (_pickerStateSync)
            {
                if (_pickerJob == null)
                    return new { ok = false, pending = false, requestId = requestId, error = "Agent에 해당 선택 작업이 없습니다. 다시 선택하세요." };
                if (!string.Equals(_pickerJob.ClientId, clientId, StringComparison.Ordinal) ||
                    !string.Equals(_pickerJob.RequestId, requestId, StringComparison.Ordinal))
                    return new { ok = false, pending = false, requestId = requestId, error = "다른 브라우저 선택 작업의 결과는 조회할 수 없습니다." };

                object response = PickerJobResponse(_pickerJob);
                if (_pickerJob.Completed) _pickerJob.Delivered = true;
                return response;
            }
        }

        private void RunPickerJob(PickerJob job)
        {
            var selectedPaths = new List<string>();
            string error = "";
            try
            {
                lock (_pickerStateSync)
                {
                    if (job.CancelRequested)
                    {
                        job.Completed = true;
                        job.Cancelled = true;
                        job.CompletedUtc = DateTime.UtcNow;
                        return;
                    }
                }

                AppendAgentLog("INFO", job.Kind == "file" ? "파일 선택 창 열림: " + job.FileType : (job.AllowMultiple ? "다중 폴더 선택 창 열림" : "폴더 선택 창 열림"));
                if (job.Kind == "file")
                {
                    string selected = NativeShellPicker.PickFile(job.InitialPath, job.FileType);
                    if (!string.IsNullOrWhiteSpace(selected)) selectedPaths.Add(selected);
                }
                else selectedPaths.AddRange(NativeShellPicker.PickFolders(job.InitialPath, job.AllowMultiple));

                AppendAgentLog("INFO", selectedPaths.Count == 0
                    ? (job.Kind == "file" ? "파일 선택 취소" : "폴더 선택 취소")
                    : (job.Kind == "file" ? "파일 선택 완료: " : "폴더 선택 완료: ") + string.Join(" | ", selectedPaths));
            }
            catch (SysException ex)
            {
                error = ex.Message;
                AppendAgentLog("ERROR", (job.Kind == "file" ? "파일" : "폴더") + " 선택 실패: " + ex.Message);
            }
            finally
            {
                lock (_pickerStateSync)
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

        private static object PickerJobResponse(PickerJob job)
        {
            if (!job.Completed)
                return new { ok = true, pending = true, requestId = job.RequestId, started = true };
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

        private object PickFolder(string body)
        {
            var data = DeserializeDictionary(body);
            string initial = GetString(data, "initialPath", "");
            string clientId = GetString(data, "clientId", "");
            bool recoverable;
            if (!TryBeginPicker(clientId, out recoverable))
                return PickerBusy(recoverable);
            try
            {
                AppendAgentLog("INFO", "폴더 선택 창 열림");
                NativeShellPicker.PrepareDialogRequest();
                string selected = NativeShellPicker.PickFolder(initial);
                AppendAgentLog("INFO", string.IsNullOrWhiteSpace(selected) ? "폴더 선택 취소" : "폴더 선택 완료: " + selected);
                return new { ok = !string.IsNullOrWhiteSpace(selected), path = selected ?? "" };
            }
            catch (SysException ex)
            {
                AppendAgentLog("ERROR", "폴더 선택 실패: " + ex.Message);
                return new { ok = false, path = "", error = ex.Message };
            }
            finally { EndPicker(clientId); }
        }

        private object PickFile(string body)
        {
            var data = DeserializeDictionary(body);
            string initial = GetString(data, "initialPath", "");
            string fileType = GetString(data, "fileType", "workspace");
            string clientId = GetString(data, "clientId", "");
            bool recoverable;
            if (!TryBeginPicker(clientId, out recoverable))
                return PickerBusy(recoverable);
            try
            {
                AppendAgentLog("INFO", "파일 선택 창 열림: " + fileType);
                NativeShellPicker.PrepareDialogRequest();
                string selected = NativeShellPicker.PickFile(initial, fileType);
                AppendAgentLog("INFO", string.IsNullOrWhiteSpace(selected) ? "파일 선택 취소" : "파일 선택 완료: " + selected);
                return new { ok = !string.IsNullOrWhiteSpace(selected), path = selected ?? "" };
            }
            catch (SysException ex)
            {
                AppendAgentLog("ERROR", "파일 선택 실패: " + ex.Message);
                return new { ok = false, path = "", error = ex.Message };
            }
            finally { EndPicker(clientId); }
        }

        private bool TryBeginPicker(string clientId, out bool recoverable)
        {
            if (!Monitor.TryEnter(_pickerSync))
            {
                lock (_pickerStateSync)
                {
                    recoverable = !string.IsNullOrWhiteSpace(clientId) &&
                        string.Equals(_pickerClientId, clientId, StringComparison.Ordinal);
                }
                return false;
            }
            lock (_pickerStateSync) _pickerClientId = clientId ?? "";
            recoverable = false;
            return true;
        }

        private void EndPicker(string clientId)
        {
            lock (_pickerStateSync)
            {
                if (string.Equals(_pickerClientId, clientId ?? "", StringComparison.Ordinal))
                    _pickerClientId = "";
            }
            Monitor.Exit(_pickerSync);
        }

        private static object PickerBusy(bool recoverable)
        {
            return new
            {
                ok = false,
                busy = true,
                recoverable = recoverable,
                path = "",
                error = "다른 파일/폴더 선택 창이 이미 열려 있습니다. 열린 창을 완료하거나 취소하세요."
            };
        }

        private object CancelPicker(string body)
        {
            var data = DeserializeDictionary(body);
            string clientId = GetString(data, "clientId", "");
            string requestId = GetString(data, "requestId", "");
            bool asyncPicker = false;
            string activeClientId;
            lock (_pickerStateSync)
            {
                if (_pickerJob != null && !_pickerJob.Completed)
                {
                    asyncPicker = true;
                    activeClientId = _pickerJob.ClientId;
                    if (!string.Equals(activeClientId, clientId, StringComparison.Ordinal) ||
                        (!string.IsNullOrWhiteSpace(requestId) && !string.Equals(_pickerJob.RequestId, requestId, StringComparison.Ordinal)))
                        return new { ok = false, cancelled = false, error = "다른 브라우저 세션의 선택 창은 취소할 수 없습니다." };
                    _pickerJob.CancelRequested = true;
                    // Explorer가 초기화 중이거나 종료 신호를 늦게 처리해도 브라우저는
                    // 즉시 대기 상태에서 벗어납니다. 늦게 끝난 이전 작업의 결과는
                    // RunPickerJob에서 이 취소 결과를 덮어쓰지 않습니다.
                    _pickerJob.Path = "";
                    _pickerJob.Paths = new List<string>();
                    _pickerJob.Error = "";
                    _pickerJob.Cancelled = true;
                    _pickerJob.Completed = true;
                    _pickerJob.CompletedUtc = DateTime.UtcNow;
                }
                else activeClientId = _pickerClientId;
            }

            if (string.IsNullOrWhiteSpace(activeClientId))
                return new { ok = true, cancelled = false, message = "열린 선택 창이 없습니다." };
            if (!asyncPicker && (string.IsNullOrWhiteSpace(clientId) || !string.Equals(activeClientId, clientId, StringComparison.Ordinal)))
                return new { ok = false, cancelled = false, error = "다른 브라우저 세션의 선택 창은 취소할 수 없습니다." };

            bool cancelled = NativeShellPicker.CancelActiveDialog();
            AppendAgentLog("INFO", cancelled
                ? "이전 파일/폴더 선택 작업을 정리했습니다."
                : "파일/폴더 선택 창 닫기 요청 시 활성 Shell Dialog가 없었습니다.");
            return new { ok = true, cancelled = cancelled };
        }

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
            lock (_vpdlSync)
            {
                if (_vpdlReservedForSimulation)
                    return new { ok = false, error = "다른 VPDL 작업이 실행 중입니다. 잠시 후 다시 시도하세요." };
                string signature = BuildRuntimePreloadSignature(req);
                if (_preloadedRuntimeControl == null || !string.Equals(_preloadedRuntimeSignature, signature, StringComparison.Ordinal))
                    return new { ok = false, error = "현재 설정에 맞는 Runtime 사전 로드가 없습니다. Workspace Runtime Structure의 Runtime File Load를 다시 실행하세요." };
                _vpdlReservedForSimulation = true;
                simulationControl = _preloadedRuntimeControl;
                _preloadedRuntimeControl = null;
                _preloadedRuntimeSignature = "";
                _preloadedRuntimeToken = "";
                _preloadedRuntimeMode = "";
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
                AppendAgentLog("START", "Simulation 시작 | Mode=" + _state.mode + " | Batch=" + _liveBatchSize + " | Output=" + (req.outputRoot ?? ""));
                Broadcast("progress", Snapshot(), true);
                _simulationTask = Task.Run(() => RunSimulation(req, simulationControl, _simulationCts.Token));
                return new { ok = true, state = Snapshot() };
            }
            catch (SysException ex)
            {
                lock (_vpdlSync)
                {
                    try
                    {
                        if (simulationControl != null)
                        {
                            RuntimeWorkspaceRegistry.Remove(simulationControl);
                            simulationControl.Dispose();
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

        private void RunSimulation(AgentStartRequest req, LocalRuntime.Control simulationControl, CancellationToken token)
        {
            bool runtimeReusable = true;
            try
            {
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
                    string cropRoot = Path.Combine(req.outputRoot, "_VisionQC_BlueCrop_Temp");
                    var blue = BuildBlueConfig(req, cropRoot, true);
                    var green = BuildGreenConfig(req, req.outputRoot, cropRoot, true);
                    var summary = IntegratedSimulationProcessor.RunStreaming(blue, green, GetIntegratedOptions(req).keepCropImages, cropRoot, simulationControl, true, progress, token);
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
                    var summary = GreenOverlayProcessor.Run(BuildGreenConfig(req, req.outputRoot, null, false), simulationControl, true, progress, token);
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
                AppendAgentLog("DONE", _state.message + " | Result=" + (_state.resultCsv ?? ""));
                Broadcast("completed", Snapshot(), true);
            }
            catch (OperationCanceledException)
            {
                lock (_sync)
                {
                    _state.running = false;
                    _state.message = "사용자에 의해 중지되었습니다.";
                }
                FlushLiveBatch();
                AppendAgentLog("STOP", _state.message);
                Broadcast("stopped", Snapshot(), true);
            }
            catch (SysException ex)
            {
                runtimeReusable = false;
                lock (_sync)
                {
                    _state.running = false;
                    _state.error = ex.ToString();
                    _state.message = "Simulation 오류: " + ex.Message;
                }
                FlushLiveBatch();
                AppendAgentLog("ERROR", _state.message);
                Broadcast("error", Snapshot(), true);
            }
            finally
            {
                lock (_vpdlSync)
                {
                    if (runtimeReusable && simulationControl != null)
                    {
                        DisposePreloadedRuntimeLocked();
                        _preloadedRuntimeControl = simulationControl;
                        _preloadedRuntimeSignature = BuildRuntimePreloadSignature(req);
                        _preloadedRuntimeToken = Guid.NewGuid().ToString("N");
                        _preloadedRuntimeMode = (req.mode ?? "green").Trim().ToLowerInvariant();
                        _licenseStatus = "Runtime Ready";
                        _runtimeMessage = "Simulation 완료 · 사전 로드 Runtime 재사용 가능";
                        simulationControl = null;
                    }
                    else
                    {
                        try
                        {
                            if (simulationControl != null)
                            {
                                RuntimeWorkspaceRegistry.Remove(simulationControl);
                                simulationControl.Dispose();
                            }
                        }
                        catch { }
                    }
                    _vpdlReservedForSimulation = false;
                }
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
                    _state.processed = Math.Max(_state.processed, _liveRecordCount);
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
                    if (m.Success)
                    {
                        _state.ok = int.Parse(m.Groups[1].Value);
                        _state.ng = int.Parse(m.Groups[2].Value);
                    }
                    // 처리 번호가 있는 진행 메시지는 아래 PROGRESS 한 줄로만 기록한다.
                    // Processor가 LiveRecord와 PrintEvery 메시지를 연달아 보내도 INFO 중복을 만들지 않는다.
                    if (!p.Processed.HasValue && p.LiveRecord == null) logMessage = p.Message;
                }
            }

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
            var enabledKeysForTools = EnabledPositions(req).Select(p => p.key).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            foreach (var tool in tools)
            {
                tool.PositionKeys = enabledKeysForTools.ToList();
                tool.UseCaTop = tool.UseCaBot = tool.UseAnTop = tool.UseAnBot = true;
            }
            var cfg = new AppConfig
            {
                OutputRoot = outputRoot,
                CellIdCsvPath = integrated ? (iopt.cellIdCsvPath ?? "") : (opt.cellIdCsvPath ?? ""),
                KeywordMode = integrated ? false : opt.keywordMode,
                KeywordInputRoot = integrated ? "" : FirstNonEmpty(GetGreenKeywordImageRoots(opt).ToArray()),
                UseGpu = opt.useGpu,
                GpuDevices = ParseGpuList(opt.gpuDevices, opt.useGpu),
                JpegQuality = Clamp(opt.jpegQuality, 1, 100, 80),
                HeatmapAlpha = Math.Max(0f, Math.Min(1f, opt.heatmapAlpha / 100f)),
                HeatmapAlphaCut = Convert.ToByte(Math.Max(0, Math.Min(255, opt.heatmapAlphaCut))),
                HeatmapImageSave = integrated ? iopt.heatmapImageSave : opt.heatmapImageSave,
                KeepSubfolders = opt.keepSubfolders,
                ForceJetWhenGrayscale = opt.forceJet,
                PrintEvery = Math.Max(1, opt.printEvery <= 0 ? 100 : opt.printEvery),
                Tools = tools,
                Judgements = judgementList
            };
            cfg.WorkspaceSlots = EnabledPositions(req).Select(p => new WorkspaceSlotConfig
            {
                Key = p.key,
                DisplayName = p.displayName,
                Enabled = true,
                WorkspacePath = FirstNonEmpty(p.greenWorkspacePath, p.workspacePath),
                InputRoots = integrated
                    ? NormalizeImageRoots(null, Path.Combine(cropRoot, p.displayName))
                    : (opt.keywordMode ? GetGreenKeywordImageRoots(opt) : GetGreenImageRoots(p)),
                InputRoot = integrated
                    ? Path.Combine(cropRoot, p.displayName)
                    : (opt.keywordMode ? FirstNonEmpty(GetGreenKeywordImageRoots(opt).ToArray()) : FirstNonEmpty(GetGreenImageRoots(p).ToArray())),
                StreamName = FirstNonEmpty(p.greenStreamName, p.streamName, "기본값"),
                Keyword = integrated ? "" : FirstNonEmpty(p.greenKeyword, p.keyword),
                Electrode = p.key != null && p.key.StartsWith("CA", StringComparison.OrdinalIgnoreCase) ? "CA" : "AN",
                Side = p.key != null && p.key.EndsWith("TOP", StringComparison.OrdinalIgnoreCase) ? "TOP" : "BOT"
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
                    elapsedSeconds = elapsed, etaSeconds = eta, imagesPerSecond = ips, batchSize = _liveBatchSize
                };
            }
        }

        private static SimulationState NewIdleState()
        {
            return new SimulationState { running = false, mode = "", current = "-", message = "Ready" };
        }

        private string DetectVpdlVersion()
        {
            string root = @"C:\Program Files\Cognex\VisionPro Deep Learning";
            foreach (string version in new[] { "4.2", "4.1", "4.0", "5.0" })
            {
                string dll = Path.Combine(root, version, "Cognex Deep Learning Studio", "ViDi.NET.Local.dll");
                if (File.Exists(dll)) return version;
            }
            return "-";
        }

        private string DetectGpuName()
        {
            try
            {
                var names = new List<string>();
                using (var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController"))
                using (var results = searcher.Get())
                {
                    foreach (ManagementObject item in results)
                    {
                        string name = Convert.ToString(item["Name"]);
                        if (!string.IsNullOrWhiteSpace(name) && name.IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) >= 0) names.Add(name);
                    }
                }
                return names.Count == 0 ? "-" : string.Join(" / ", names);
            }
            catch { return "-"; }
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

        private async Task WriteResponse(NetworkStream stream, int status, string contentType, string body, string origin)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(body ?? "");
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
            try { NativeShellPicker.CancelActiveDialog(); } catch { }
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
