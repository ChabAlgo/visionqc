using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace VisionQC.GreenCompare
{
    internal static class Program
    {
        [STAThread] private static int Main(string[] args)
        {
            string home = Environment.GetEnvironmentVariable("VISIONQC_AGENT_HOME")
                ?? Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", ".."));
            // Headless mode is for regression tests/explicit local operator use only.
            if (args.Length == 2 && args[0] == "--run-seed") {
                try { Console.WriteLine(Comparison.Run(args[1], home, AppDomain.CurrentDomain.BaseDirectory, Console.WriteLine, CancellationToken.None)); return 0; }
                catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
            }
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new CompareForm(home));
            return 0;
        }
    }

    internal sealed class CompareForm : Form
    {
        private readonly TextBox text = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
        private readonly Button start = new Button { Text = "비교 시작", Width = 110 };
        private readonly Button stop = new Button { Text = "중지", Width = 80, Enabled = false };
        private readonly string home;
        private CancellationTokenSource cancellation;
        internal CompareForm(string home)
        {
            this.home = home; Text = "VisionQC Green 원본 비교 진단"; Width = 840; Height = 560;
            Font = new System.Drawing.Font("Malgun Gothic", 10);
            text.Text = "최근 Green 검사에 사용한 이미지 1장과 Workspace를 자동으로 선택합니다.\r\n"
                + "수정하지 않은 원본 엔진 / 현재 엔진 × 원본 DLL 검색 / 고정 DLL 검색: 총 4회 순차 검사합니다.\r\n"
                + "진단 결과는 서버 내부 logs에만 저장하며 검사 이력 DB에는 넣지 않습니다.\r\n"
                + "다른 프로그램을 종료하거나 GPU/드라이버 설정을 변경하지 않습니다.\r\n"
                + "먼저 VisionQC의 진행 중인 검사를 끝내고 비교 시작을 누르세요.\r\n";
            var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48 };
            bar.Controls.Add(start); bar.Controls.Add(stop); Controls.Add(text); Controls.Add(bar);
            start.Click += async (s, e) => {
                try {
                    CheckIdle();
                    string seed = Comparison.LatestSeed(home);
                    if (MessageBox.Show(this, "최근 검사 이미지 1장으로 비교 진단을 시작할까요?\r\n"
                        + "원본 파일은 변경하지 않으며 서버 내부 진단 폴더에 이미지 사본 1장이 저장됩니다.",
                        Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
                    cancellation = new CancellationTokenSource(); start.Enabled = false; stop.Enabled = true;
                    string report = await Task.Run(() => Comparison.Run(seed, home, AppDomain.CurrentDomain.BaseDirectory,
                        value => BeginInvoke((Action)(() => text.AppendText(value + "\r\n"))), cancellation.Token));
                    text.AppendText("\r\n" + File.ReadAllText(report));
                    Process.Start(new ProcessStartInfo { FileName = "notepad.exe", Arguments = "\"" + report + "\"", UseShellExecute = true });
                } catch (OperationCanceledException) { text.AppendText("\r\n중지했습니다. 완료된 진단 기록은 보존됩니다.\r\n"); }
                  catch (Exception ex) { text.AppendText("\r\n진단 오류: " + ex.Message + "\r\n"); }
                finally { cancellation?.Dispose(); cancellation = null; start.Enabled = true; stop.Enabled = false; }
            };
            stop.Click += (s, e) => cancellation?.Cancel();
            FormClosing += (s, e) => { if (cancellation != null) { cancellation.Cancel(); e.Cancel = true; } };
        }
        private static void CheckIdle()
        {
            var request = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:17891/api/status");
            request.Proxy = null; request.Timeout = 3000;
            try {
                using (var response = request.GetResponse())
                using (var reader = new StreamReader(response.GetResponseStream())) {
                    var data = Comparison.Json().Deserialize<Dictionary<string, object>>(reader.ReadToEnd());
                    if (data.ContainsKey("running") && Convert.ToBoolean(data["running"]))
                        throw new InvalidOperationException("VisionQC 검사가 진행 중입니다. 종료 후 진단하세요.");
                }
            } catch (WebException) { throw new InvalidOperationException("VisionQC Agent를 켜고 검사가 정지된 상태에서 다시 실행하세요."); }
        }
    }

    internal static class Comparison
    {
        internal static JavaScriptSerializer Json() { return new JavaScriptSerializer { MaxJsonLength = 30 * 1024 * 1024 }; }
        internal static string LatestSeed(string home)
        {
            string root = Path.Combine(home, "logs", "green-runs");
            if (!Directory.Exists(root)) throw new FileNotFoundException("새 버전으로 Green 검사를 한 번 실행한 뒤 비교 진단을 시작하세요.");
            string found = Directory.GetDirectories(root).Select(d => Path.Combine(d, "last-green-input.json"))
                .Where(File.Exists).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
            if (found == null) throw new FileNotFoundException("비교할 이미지 기록이 없습니다. 상세 진단 로그를 켜고 Green 검사를 한 번 실행하세요.");
            return found;
        }

        internal static string Run(string seedPath, string home, string binaries, Action<string> progress, CancellationToken token)
        {
            bool owner;
            using (var mutex = new Mutex(true, "Local\\VisionQC.GreenCompare", out owner)) {
                if (!owner) throw new InvalidOperationException("이미 비교 진단이 실행 중입니다.");
                try { return RunOwned(seedPath, home, binaries, progress, token); }
                finally { mutex.ReleaseMutex(); }
            }
        }

        private static string RunOwned(string seedPath, string home, string binaries, Action<string> progress, CancellationToken token)
        {
            var json = Json();
            var seed = json.Deserialize<Dictionary<string, object>>(File.ReadAllText(seedPath));
            string request = Path.Combine(Path.GetDirectoryName(seedPath), "request.json");
            var config = json.Deserialize<Dictionary<string, object>>(File.ReadAllText(request));
            string image = Convert.ToString(seed["ImagePath"]), slotKey = Convert.ToString(seed["SlotKey"]);
            string api = Convert.ToString(seed["ApiVersion"]), studio = Convert.ToString(seed["Studio"]);
            Version apiVersion;
            if (!Version.TryParse(api, out apiVersion) || apiVersion.ToString(2) != api)
                throw new InvalidDataException("VPDL API 버전 기록이 올바르지 않습니다.");
            if (!File.Exists(image) || !File.Exists(Path.Combine(studio, "ViDi.NET.Local.dll")))
                throw new FileNotFoundException("기록된 이미지 또는 VPDL 설치 경로를 찾지 못했습니다.");
            // Do not silently test another installed API or Universal instead of the requested API.
            string exact = Path.Combine(home, "Workers", api);
            if (File.Exists(Path.Combine(exact, "VisionQC.GreenBaseline.exe"))) binaries = exact;
            if (!File.Exists(Path.Combine(binaries, "VisionQC.GreenBaseline.exe"))
                || !File.Exists(Path.Combine(binaries, "VisionQC.GreenRunner.exe")))
                throw new FileNotFoundException("Green 비교 실행기가 없습니다. 새 Agent를 설치하세요.");
            string root = Path.Combine(home, "logs", "green-comparisons", DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string input = Path.Combine(root, "input"); Directory.CreateDirectory(input);
            string copy = Path.Combine(input, Path.GetFileName(image));
            File.Copy(image, copy, false);
            string imageHash = Hash(copy);
            if (imageHash != Hash(image)) throw new IOException("이미지가 복사 중 변경됐습니다. 결과 비교를 중단합니다.");
            Normalize(config, slotKey, input);
            var slot = Dict(Items(config["WorkspaceSlots"]).Single());
            string workspace = Convert.ToString(slot["WorkspacePath"]), workspaceHash = Hash(workspace);
            var rows = new List<Dictionary<string, object>>();
            File.WriteAllText(Path.Combine(root, "inputs.json"), json.Serialize(new {
                SourceImage = image, ImageSHA256 = imageHash, Workspace = workspace, WorkspaceSHA256 = workspaceHash,
                GpuDevices = config["GpuDevices"], ApiVersion = api, Studio = studio,
                Note = "One image/one position; Cell ID and keyword filters bypassed only for this diagnostic. No DB import." }));
            string[] labels = { "A 원본 엔진 / 원본 DLL 검색", "B 원본 엔진 / 고정 DLL 검색", "C 현재 엔진 / 원본 DLL 검색", "D 현재 엔진 / 고정 DLL 검색" };
            string[] ids = { "A-baseline-original", "B-baseline-pinned", "C-current-original", "D-current-pinned" };
            try {
                for (int i = 0; i < 4; i++) {
                    token.ThrowIfCancellationRequested();
                    string dir = Path.Combine(root, ids[i]); Directory.CreateDirectory(dir);
                    config["OutputRoot"] = Path.Combine(dir, "output");
                    string caseRequest = Path.Combine(dir, "request.json");
                    File.WriteAllText(caseRequest, json.Serialize(config), new UTF8Encoding(false));
                    string mode = i % 2 == 0 ? "original" : "pinned";
                    string exe = Path.Combine(binaries, i < 2 ? "VisionQC.GreenBaseline.exe" : "VisionQC.GreenRunner.exe");
                    progress(labels[i] + " 실행 중...");
                    var row = RunCase(exe, caseRequest, dir, mode, i < 2, api, studio, home, token);
                    row["case"] = labels[i]; rows.Add(row);
                    progress(labels[i] + ": " + row["status"] + " / Exit=" + row["exit"]);
                    WriteReport(root, rows, workspaceHash == Hash(workspace), imageHash, workspaceHash);
                }
                return WriteReport(root, rows, workspaceHash == Hash(workspace), imageHash, workspaceHash);
            } catch {
                WriteReport(root, rows, workspaceHash == Hash(workspace), imageHash, workspaceHash);
                throw;
            }
        }

        internal static void Normalize(Dictionary<string, object> config, string slotKey, string input)
        {
            var slots = Items(config["WorkspaceSlots"]).Select(Dict).Where(s => Convert.ToString(s["Key"]) == slotKey).ToArray();
            if (slots.Length != 1) throw new InvalidDataException("검사 Position 기록이 모호합니다.");
            var slot = slots[0];
            // Original engine has four fixed slots; remap diagnostic-only slot/tool membership
            // identically for both engines. Workspace/stream/tool/ROI/threshold/GPU remain unchanged.
            var tools = Items(config["Tools"]).Select(Dict).Where(t => Applies(t, slotKey)).ToArray();
            if (tools.Length == 0) throw new InvalidDataException("비교할 Tool이 없습니다.");
            foreach (var tool in tools) {
                tool["PositionKeys"] = new[] { "CA_TOP" }; tool["UseCaTop"] = true;
                tool["UseCaBot"] = false; tool["UseAnTop"] = false; tool["UseAnBot"] = false;
            }
            slot["Key"] = "CA_TOP"; slot["Enabled"] = true; slot["InputRoot"] = input;
            slot["InputRoots"] = new[] { input }; slot["Keyword"] = "";
            config["WorkspaceSlots"] = new[] { slot }; config["Tools"] = tools;
            config["KeywordMode"] = false; config["KeywordInputRoot"] = input; config["CellIdCsvPath"] = null;
            config["OriginalExecution"] = true; config["DetailedDiagnostics"] = true;
            config["DisableTensorRt"] = false; config["DisableOptimizedGpuMemory"] = false;
        }
        private static bool Applies(Dictionary<string, object> tool, string key)
        {
            object keys;
            if (tool.TryGetValue("PositionKeys", out keys) && keys != null)
                return Items(keys).Any(x => string.Equals(Convert.ToString(x), key, StringComparison.OrdinalIgnoreCase));
            string flag = key == "CA_TOP" ? "UseCaTop" : key == "CA_BOT" ? "UseCaBot" : key == "AN_TOP" ? "UseAnTop" : key == "AN_BOT" ? "UseAnBot" : null;
            return flag != null && tool.ContainsKey(flag) && Convert.ToBoolean(tool[flag]);
        }
        private static Dictionary<string, object> Dict(object value) { return (Dictionary<string, object>)value; }
        private static IEnumerable<object> Items(object value) { return ((IEnumerable)value).Cast<object>(); }

        private static Dictionary<string, object> RunCase(string exe, string request, string dir, string mode, bool baseline,
            string api, string studio, string home, CancellationToken token)
        {
            var start = new ProcessStartInfo { FileName = exe, WorkingDirectory = Path.GetDirectoryName(exe),
                Arguments = baseline ? "\"" + request + "\" " + mode : "--request \"" + request + "\" --diagnostic-search " + mode,
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true, RedirectStandardError = true };
            start.EnvironmentVariables["COGNEX_VPDL_DLL_DIR"] = studio;
            start.EnvironmentVariables["VISIONQC_VPDL_API_VERSION"] = api;
            start.EnvironmentVariables["VISIONQC_AGENT_HOME"] = dir;
            // Both cases start from the same inherited Windows environment. Each harness applies
            // its explicit search mode; no system/user environment is modified.
            start.EnvironmentVariables.Remove("VISIONQC_ORIGINAL_PATH");
            var modules = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            var watch = Stopwatch.StartNew();
            bool timedOut = false;
            int code;
            using (var child = new Process { StartInfo = start }) {
                child.Start();
                var stdout = child.StandardOutput.ReadToEndAsync();
                var stderr = child.StandardError.ReadToEndAsync();
                try {
                    while (!child.WaitForExit(250)) {
                        token.ThrowIfCancellationRequested();
                        CaptureModules(child, modules, dir);
                        if (watch.Elapsed.TotalSeconds > 180) { timedOut = true; child.Kill(); break; }
                    }
                    child.WaitForExit(); code = child.ExitCode;
                    File.WriteAllText(Path.Combine(dir, "stdout.txt"), stdout.GetAwaiter().GetResult());
                    File.WriteAllText(Path.Combine(dir, "stderr.txt"), stderr.GetAwaiter().GetResult());
                } finally {
                    try { if (!child.HasExited) { child.Kill(); child.WaitForExit(5000); } } catch { }
                }
            }
            string resultFile = Path.Combine(dir, "result.json");
            bool completed = false;
            if (code == 0 && File.Exists(resultFile)) {
                var result = Json().Deserialize<Dictionary<string, object>>(File.ReadAllText(resultFile));
                object total;
                completed = result.TryGetValue("TotalImages", out total) && Convert.ToInt32(total) == 1;
            }
            var row = new Dictionary<string, object> { ["status"] = timedOut ? "TIMEOUT" : completed ? "PASS" : "FAIL",
                ["exit"] = code + " (0x" + unchecked((uint)code).ToString("X8") + ")",
                ["seconds"] = watch.Elapsed.TotalSeconds, ["directory"] = dir, ["modules"] = modules,
                ["sdkFailure"] = ReadIfExists(dir, "first-sdk-failure.txt") + ReadIfExists(dir, "last-sdk-failure.txt"),
                ["cleanup"] = ReadIfExists(dir, "last-cleanup-stage.txt"),
                ["moduleCount"] = modules.Count, ["moduleReadErrors"] = ReadIfExists(dir, "module-read-errors.txt") };
            File.WriteAllText(Path.Combine(dir, "case.json"), Json().Serialize(row));
            return row;
        }

        private static void CaptureModules(Process child, Dictionary<string, object> modules, string dir)
        {
            try {
                child.Refresh(); // Process.Modules is cached; inference can load DLLs after startup.
                foreach (ProcessModule module in child.Modules) {
                    string path = module.FileName;
                    if (modules.ContainsKey(path)) continue;
                    string hash;
                    try { hash = Hash(path); } catch (Exception ex) { hash = "unavailable: " + ex.GetType().Name; }
                    modules[path] = new { name = module.ModuleName, path, version = module.FileVersionInfo.FileVersion, sha256 = hash };
                }
                File.WriteAllText(Path.Combine(dir, "modules.json"), Json().Serialize(modules));
            } catch (Exception ex) { File.AppendAllText(Path.Combine(dir, "module-read-errors.txt"), ex.Message + Environment.NewLine); }
        }
        private static string ReadIfExists(string dir, string name)
        { string file = Path.Combine(dir, name); return File.Exists(file) ? File.ReadAllText(file) : ""; }
        private static string Hash(string file)
        {
            using (var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "");
        }
        private static string WriteReport(string root, List<Dictionary<string, object>> rows, bool unchanged, string imageHash, string workspaceHash)
        {
            File.WriteAllText(Path.Combine(root, "comparison.json"), Json().Serialize(new { WorkspaceUnchanged = unchanged, ImageSHA256 = imageHash, WorkspaceSHA256 = workspaceHash, Cases = rows }));
            var report = new StringBuilder("VisionQC Green 원본 비교 진단\r\n\r\n");
            report.AppendLine("같은 이미지 1장·Workspace·GPU 설정. 검사 결과는 DB에 저장하지 않았습니다.");
            report.AppendLine("원본 엔진 두 소스는 수정하지 않았습니다. 원본 GUI 실행 파일 전체와 동일하다는 뜻은 아닙니다.");
            report.AppendLine("Workspace 변경 여부: " + (unchanged ? "변경 없음" : "변경됨 — 비교 무효"));
            foreach (var row in rows) {
                report.AppendLine(); report.AppendLine(row["case"] + ": " + row["status"] + " | Exit=" + row["exit"]);
                string failure = Convert.ToString(row["sdkFailure"]);
                if (failure.Length > 0) report.AppendLine(failure.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(x => x.Contains("Exception")) ?? failure.Split('\n')[0]);
                if (Convert.ToString(row["cleanup"]).Length > 0) report.AppendLine("정리 단계: " + row["cleanup"]);
            }
            report.AppendLine("\r\n해석 기준 (원인 확정이 아니라 구분 실험):");
            report.AppendLine("A/C 성공, B/D 실패 → 고정 DLL 검색 영향 우선 확인.");
            report.AppendLine("A/B 성공, C/D 실패 → 현재 검사 엔진/진단/호스트 차이 우선 확인.");
            report.AppendLine("네 경우 모두 실패 + 실제 원본 GUI 정상 → 원본 GUI의 실제 DLL/환경과 비교 필요.");
            report.AppendLine("네 경우 모두 성공 → 단일 이미지 조건에서 미재현. 전체 실행/시점 차이 확인.");
            report.AppendLine("네 경우 외 패턴은 SDK 실패/모듈 기록을 함께 확인. 성공해도 판정 일치까지 자동 보장하지 않습니다.");
            report.AppendLine("\r\n위 결과 화면을 확인하세요. 상세 modules.json/요청/이미지 사본은 서버 안에만 보관하세요.");
            report.AppendLine("결과 폴더: " + root);
            string path = Path.Combine(root, "결과요약.txt"); File.WriteAllText(path, report.ToString(), Encoding.UTF8); return path;
        }
    }
}
