using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using VisionQC.LocalAgent.Domain;
using VisionQC.LocalAgent.Persistence;
using VisionQC.LocalAgent.Services;

namespace VisionQC.LocalAgent
{
    internal sealed class CoreAgentServer : IDisposable
    {
        private const int Port = 17891;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        private readonly object _sync = new object();
        private readonly List<SseClient> _sse = new List<SseClient>();
        private readonly CancellationTokenSource _serverCts = new CancellationTokenSource();
        private readonly string _instanceId = Guid.NewGuid().ToString("N");
        private readonly PickerService _picker;
        private readonly ImagePreviewService _imagePreview;
        private readonly SqliteRunStore _historyStore;
        private readonly HistoryService _history;
        private TcpListener _listener;

        internal CoreAgentServer()
        {
            _picker = new PickerService(AppendAgentLog);
            _imagePreview = new ImagePreviewService();
            _historyStore = new SqliteRunStore(ResolveHistoryDatabasePath());
            _history = new HistoryService(_historyStore, _json);
        }

        internal void RunUntilExit(bool openOfflinePage)
        {
            _listener = new TcpListener(IPAddress.Loopback, Port);
            try { _listener.Start(); }
            catch (SocketException ex)
            {
                MessageBox.Show("VisionQC Local Agent가 이미 실행 중이거나 포트 17891을 사용할 수 없습니다.\r\n\r\n" + ex.Message,
                    "VisionQC Local Agent", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            Task.Run(() => AcceptLoop(_serverCts.Token));
            if (openOfflinePage) Task.Run(OpenOfflinePage);
            while (!_serverCts.IsCancellationRequested) Thread.Sleep(250);
        }

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
                HttpRequest request = await ReadRequest(stream).ConfigureAwait(false);
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
                    await WriteSse(sse, "status", IdleState()).ConfigureAwait(false);
                    return;
                }

                object result;
                int status = 200;
                switch (request.Path)
                {
                    case "/api/status": result = BuildStatus(); break;
                    case "/api/vpdl/versions": result = BuildVpdlVersions(); break;
                    case "/api/vpdl/select": result = SelectVpdlWorker(request.Body); break;
                    case "/api/runtime/check":
                    case "/api/runtime/preload":
                    case "/api/workspace/inspect":
                    case "/api/blue/fallback/preview":
                    case "/api/classification/inspect":
                    case "/api/classification/inspect-upload":
                    case "/api/simulation/start": result = VpdlUnavailable(); break;
                    case "/api/simulation/stop": result = new { ok = true, state = IdleState() }; break;
                    case "/api/simulation/state": result = IdleState(); break;
                    case "/api/pick/start": result = _picker.Start(DeserializeDictionary(request.Body)); break;
                    case "/api/pick/status": result = _picker.Status(DeserializeDictionary(request.Body)); break;
                    case "/api/pick/folder": result = _picker.PickFolder(DeserializeDictionary(request.Body)); break;
                    case "/api/pick/file": result = _picker.PickFile(DeserializeDictionary(request.Body)); break;
                    case "/api/pick/cancel": result = _picker.Cancel(DeserializeDictionary(request.Body)); break;
                    case "/api/naming/preview": result = PreviewNamingProfile(request.Body); break;
                    case "/api/image/preview": result = PreviewImage(request.Body); break;
                    case "/api/history/import": result = _history.ImportBrowserRows(request.Body); break;
                    case "/api/history/search": result = _history.Search(request.Body); break;
                    case "/api/history/import-file/start": result = _history.StartFileImport(request.Body); break;
                    case "/api/history/import-file/status": result = _history.FileImportStatus(request.Body); break;
                    case "/api/history/delete": result = _history.DeleteAll(request.Body); break;
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
            catch (Exception ex)
            {
                if (!IsExpectedClientDisconnect(ex)) AppendAgentLog("ERROR", "HTTP 요청 처리 실패: " + ex.Message);
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
            return new
            {
                ok = true,
                vpdlAvailable = false,
                instanceId = _instanceId,
                agentVersion = Program.AgentVersion,
                engineVersion = "VisionQC Core · VPDL 미설치 모드",
                installedVpdlVersion = "-",
                activeVpdlApiVersion = "-",
                vpdlWorkerMode = "none",
                availableVpdlVersions = AvailableVpdlVersions(),
                vpdlVersion = "-",
                license = "VPDL 미설치",
                runtimeMessage = "분석·검사 이력·이미지 조회·분류 사용 가능 · Runtime Load와 Simulation은 VPDL 설치 후 사용하세요.",
                gpu = "-",
                running = false,
                runtimePreloaded = false,
                runtimePreloadMode = "",
                runtimePreloadToken = "",
                runtimePreloadSignature = "",
                runtimePreloadControlSignature = "",
                runtimePreloadGreenWorkspaceSignature = "",
                historyDatabasePath = _historyStore.DatabasePath,
                state = IdleState()
            };
        }

        private object BuildVpdlVersions()
        {
            return new
            {
                ok = true,
                vpdlAvailable = false,
                activeApiVersion = "-",
                activeProductVersion = "-",
                workerMode = "none",
                selectedApiVersion = VpdlWorkerSelection.Read(),
                available = AvailableVpdlVersions()
            };
        }

        private object[] AvailableVpdlVersions()
        {
            return VpdlRuntimeCatalog.Discover().Select(item => (object)new
            {
                productVersion = item.ProductVersion,
                apiVersion = item.ApiVersion,
                displayName = item.DisplayName,
                workerInstalled = VpdlWorkerLocator.IsAvailable(Program.AgentHomeDirectory, item.ApiVersion)
            }).ToArray();
        }

        private object SelectVpdlWorker(string body)
        {
            Dictionary<string, object> request = DeserializeDictionary(body);
            string requested = GetString(request, "apiVersion", GetString(request, "version", ""));
            VpdlRuntimeCatalog.Installation target = VpdlRuntimeCatalog.FindByVersion(requested);
            if (target == null) return new { ok = false, error = "선택한 VPDL 버전이 정상 설치본으로 확인되지 않습니다: " + requested };
            if (!VpdlWorkerLocator.IsAvailable(Program.AgentHomeDirectory, target.ApiVersion))
                return new { ok = false, error = "VPDL " + target.ProductVersion + "용 Worker가 설치되어 있지 않습니다." };
            VpdlWorkerSelection.Write(target.ApiVersion);
            _ = Task.Run(async () =>
            {
                await Task.Delay(250).ConfigureAwait(false);
                Program.RequestWorkerRestart();
                _serverCts.Cancel();
            });
            return new { ok = true, restarting = true, selectedApiVersion = target.ApiVersion, selectedVpdlVersion = target.ProductVersion };
        }

        private object VpdlUnavailable()
        {
            return new
            {
                ok = false,
                vpdlAvailable = false,
                installedVpdlVersion = "-",
                vpdlVersion = "-",
                license = "VPDL 미설치",
                error = "Cognex VPDL Runtime이 설치되어 있지 않습니다. 현재 기능은 VPDL 설치 후 Agent를 다시 실행하면 사용할 수 있습니다."
            };
        }

        private static SimulationState IdleState()
        {
            return new SimulationState { running = false, mode = "", current = "-", message = "VPDL 미설치 · 분석 및 분류 기능 사용 가능" };
        }

        private NamingPreviewResponse PreviewNamingProfile(string body)
        {
            try
            {
                NamingPreviewRequest request = _json.Deserialize<NamingPreviewRequest>(body ?? "{}") ?? new NamingPreviewRequest();
                if (request.fileNames != null && request.fileNames.Count > 200) request.fileNames = request.fileNames.Take(200).ToList();
                return NamingProfileParser.Preview(request);
            }
            catch (Exception ex) { return new NamingPreviewResponse { ok = false, error = "파일명 규칙 미리보기 실패: " + ex.Message }; }
        }

        private AgentImagePreviewResponse PreviewImage(string body)
        {
            try { return _imagePreview.Create(_json.Deserialize<AgentImagePreviewRequest>(body ?? "{}") ?? new AgentImagePreviewRequest()); }
            catch (Exception ex) { return new AgentImagePreviewResponse { ok = false, error = "이미지 미리보기 요청 오류: " + ex.Message }; }
        }

        private Dictionary<string, object> DeserializeDictionary(string body)
        {
            try { return _json.Deserialize<Dictionary<string, object>>(body ?? "{}") ?? new Dictionary<string, object>(); }
            catch { return new Dictionary<string, object>(); }
        }

        private static string GetString(Dictionary<string, object> data, string key, string fallback)
        {
            object value;
            return data != null && data.TryGetValue(key, out value) && value != null ? Convert.ToString(value) : fallback;
        }

        private void AppendAgentLog(string level, string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            BroadcastObject("log", new
            {
                time = DateTime.Now.ToString("HH:mm:ss.fff"),
                level = string.IsNullOrWhiteSpace(level) ? "INFO" : level.Trim().ToUpperInvariant(),
                message = message,
                state = IdleState()
            });
        }

        private void BroadcastObject(string eventName, object data)
        {
            string payload = _json.Serialize(data);
            List<SseClient> clients;
            lock (_sync) clients = _sse.ToList();
            foreach (SseClient client in clients)
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
                headerEnd = FindHeaderEnd(data.ToArray());
            }
            if (headerEnd < 0) return null;
            byte[] all = data.ToArray();
            string head = Encoding.UTF8.GetString(all, 0, headerEnd);
            string[] lines = head.Split(new[] { "\r\n" }, StringSplitOptions.None);
            string[] first = lines[0].Split(' ');
            var request = new HttpRequest { Method = first[0].ToUpperInvariant(), Path = first.Length > 1 ? first[1].Split('?')[0] : "/" };
            for (int index = 1; index < lines.Length; index++)
            {
                int colon = lines[index].IndexOf(':');
                if (colon > 0) request.Headers[lines[index].Substring(0, colon).Trim().ToLowerInvariant()] = lines[index].Substring(colon + 1).Trim();
            }
            int contentLength = 0;
            if (request.Headers.ContainsKey("content-length")) int.TryParse(request.Headers["content-length"], out contentLength);
            int bodyStart = headerEnd + 4;
            var body = new MemoryStream();
            if (all.Length > bodyStart) body.Write(all, bodyStart, all.Length - bodyStart);
            while (body.Length < contentLength)
            {
                int read = await stream.ReadAsync(buffer, 0, Math.Min(buffer.Length, contentLength - (int)body.Length)).ConfigureAwait(false);
                if (read <= 0) break;
                body.Write(buffer, 0, read);
            }
            request.Body = Encoding.UTF8.GetString(body.ToArray());
            return request;
        }

        private static int FindHeaderEnd(byte[] data)
        {
            for (int index = 0; index <= data.Length - 4; index++)
                if (data[index] == 13 && data[index + 1] == 10 && data[index + 2] == 13 && data[index + 3] == 10) return index;
            return -1;
        }

        private async Task OpenOfflinePage()
        {
            await Task.Delay(350).ConfigureAwait(false);
            try { Process.Start(new ProcessStartInfo { FileName = "http://127.0.0.1:" + Port + "/", UseShellExecute = true }); }
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
                if ((!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && !string.Equals(path, root, StringComparison.OrdinalIgnoreCase)) || !File.Exists(path))
                {
                    await WriteResponse(stream, 404, "text/html; charset=utf-8", notFound, origin).ConfigureAwait(false);
                    return;
                }
                await WriteBytesResponse(stream, 200, OfflineContentType(path), File.ReadAllBytes(path), origin).ConfigureAwait(false);
            }
            catch { await WriteResponse(stream, 500, "text/html; charset=utf-8", notFound, origin).ConfigureAwait(false); }
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

        private async Task WriteJson(NetworkStream stream, int status, object data, string origin)
        {
            await WriteResponse(stream, status, "application/json; charset=utf-8", _json.Serialize(data), origin).ConfigureAwait(false);
        }

        private async Task WriteResponse(NetworkStream stream, int status, string contentType, string body, string origin)
        {
            await WriteBytesResponse(stream, status, contentType, Encoding.UTF8.GetBytes(body ?? ""), origin).ConfigureAwait(false);
        }

        private async Task WriteBytesResponse(NetworkStream stream, int status, string contentType, byte[] bytes, string origin)
        {
            bytes = bytes ?? new byte[0];
            string reason = status == 200 ? "OK" : status == 204 ? "No Content" : status == 404 ? "Not Found" : status >= 500 ? "Internal Server Error" : "Error";
            string headers = "HTTP/1.1 " + status + " " + reason + "\r\nContent-Type: " + contentType + "\r\nContent-Length: " + bytes.Length + "\r\n" + CorsHeaders(origin) + "Connection: close\r\n\r\n";
            byte[] head = Encoding.ASCII.GetBytes(headers);
            await stream.WriteAsync(head, 0, head.Length).ConfigureAwait(false);
            if (bytes.Length > 0) await stream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
        }

        private async Task WriteSseHeaders(NetworkStream stream, string origin)
        {
            string headers = "HTTP/1.1 200 OK\r\nContent-Type: text/event-stream; charset=utf-8\r\nCache-Control: no-cache\r\n" + CorsHeaders(origin) + "Connection: keep-alive\r\n\r\n";
            byte[] head = Encoding.ASCII.GetBytes(headers);
            await stream.WriteAsync(head, 0, head.Length).ConfigureAwait(false);
        }

        private async Task WriteSse(SseClient client, string eventName, object data)
        {
            byte[] bytes = Encoding.UTF8.GetBytes("event: " + eventName + "\ndata: " + _json.Serialize(data) + "\n\n");
            await client.Stream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
        }

        private static string CorsHeaders(string origin)
        {
            string allowed = IsAllowedOrigin(origin) ? origin : "https://chabalgo.github.io";
            return "Access-Control-Allow-Origin: " + allowed + "\r\nVary: Origin\r\nAccess-Control-Allow-Methods: GET, POST, OPTIONS\r\nAccess-Control-Allow-Headers: Content-Type\r\nAccess-Control-Allow-Private-Network: true\r\n";
        }

        private static bool IsAllowedOrigin(string origin)
        {
            return string.IsNullOrWhiteSpace(origin)
                || string.Equals(origin, "https://chabalgo.github.io", StringComparison.OrdinalIgnoreCase)
                || origin.StartsWith("http://127.0.0.1:", StringComparison.OrdinalIgnoreCase)
                || origin.StartsWith("http://localhost:", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsExpectedClientDisconnect(Exception ex)
        {
            if (ex is SocketException || (ex is IOException && ex.InnerException is SocketException)) return true;
            string message = (ex.Message ?? "").ToLowerInvariant();
            return message.Contains("forcibly closed") || message.Contains("connection was aborted") || message.Contains("전송 연결") || message.Contains("사용자의 호스트 시스템");
        }

        public void Dispose()
        {
            try { _picker.Dispose(); } catch { }
            try { _history.Dispose(); } catch { }
            try { _historyStore.Dispose(); } catch { }
            try { _serverCts.Cancel(); } catch { }
            try { _listener?.Stop(); } catch { }
            lock (_sync)
            {
                foreach (SseClient client in _sse) try { client.Client.Close(); } catch { }
                _sse.Clear();
            }
        }

        private sealed class HttpRequest
        {
            internal string Method;
            internal string Path;
            internal string Body;
            internal readonly Dictionary<string, string> Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class SseClient
        {
            internal readonly TcpClient Client;
            internal readonly NetworkStream Stream;
            internal readonly object Sync = new object();
            internal SseClient(TcpClient client, NetworkStream stream) { Client = client; Stream = stream; }
        }
    }
}
