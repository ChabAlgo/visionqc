using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace VisionQC.AgentInstaller
{
    internal static class Program
    {
        private const string AgentExe = "VisionQC.LocalAgent.exe";
        private const string ProductVersion = "1.3.5";
        private static readonly PayloadFile[] Payload =
        {
            new PayloadFile("VisionQC.AgentInstaller.Payload.Launcher.VisionQC.LocalAgent.exe", AgentExe),
            new PayloadFile("VisionQC.AgentInstaller.Payload.WorkerManifest.vpdl-workers.json", "vpdl-workers.json"),
            new PayloadFile("VisionQC.AgentInstaller.Payload.WorkerBundle.vpdl-workers.zip", "vpdl-workers.zip"),
            new PayloadFile("VisionQC.AgentInstaller.Payload.Web.index.html", "Web\\index.html"),
            new PayloadFile("VisionQC.AgentInstaller.Payload.Web.visionqc-extension.js", "Web\\visionqc-extension.js"),
            new PayloadFile("VisionQC.AgentInstaller.Payload.Web.visionqc-extension.css", "Web\\visionqc-extension.css"),
            new PayloadFile("VisionQC.AgentInstaller.Payload.Web.visionqc-v470.css", "Web\\visionqc-v470.css"),
            new PayloadFile("VisionQC.AgentInstaller.Payload.Web.visionqc-v4433-clean.css", "Web\\visionqc-v4433-clean.css"),
            new PayloadFile("VisionQC.AgentInstaller.Payload.Web.assets.index-v4.4.33.js", "Web\\assets\\index-v4.4.33.js"),
            new PayloadFile("VisionQC.AgentInstaller.Payload.Web.assets.jszip.min.js", "Web\\assets\\jszip.min.js"),
            new PayloadFile("VisionQC.AgentInstaller.Payload.Web.assets.tailwind-offline.css", "Web\\assets\\tailwind-offline.css"),
            new PayloadFile("VisionQC.AgentInstaller.Payload.Web.assets.toptec-logo.png", "Web\\assets\\toptec-logo.png"),
            new PayloadFile("VisionQC.AgentInstaller.Payload.Web.assets.fonts.inter-latin-300-normal.woff2", "Web\\assets\\fonts\\inter-latin-300-normal.woff2"),
            new PayloadFile("VisionQC.AgentInstaller.Payload.Web.assets.fonts.inter-latin-400-normal.woff2", "Web\\assets\\fonts\\inter-latin-400-normal.woff2"),
            new PayloadFile("VisionQC.AgentInstaller.Payload.Web.assets.fonts.inter-latin-500-normal.woff2", "Web\\assets\\fonts\\inter-latin-500-normal.woff2"),
            new PayloadFile("VisionQC.AgentInstaller.Payload.Web.assets.fonts.inter-latin-600-normal.woff2", "Web\\assets\\fonts\\inter-latin-600-normal.woff2"),
            new PayloadFile("VisionQC.AgentInstaller.Payload.Web.assets.fonts.inter-latin-700-normal.woff2", "Web\\assets\\fonts\\inter-latin-700-normal.woff2")
        };

        [STAThread]
        private static int Main(string[] args)
        {
            bool silent = args != null && args.Any(arg => string.Equals(arg, "--silent", StringComparison.OrdinalIgnoreCase));
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            string silentErrorLog = Path.Combine(Path.GetTempPath(), "VisionQC_Agent_Installer.error.log");
            if (silent) try { if (File.Exists(silentErrorLog)) File.Delete(silentErrorLog); } catch { }
            try
            {
                string installDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VisionQC", "LocalAgent");
                StopRunningAgent(Path.Combine(installDir, AgentExe));
                ExtractPayload(installDir);
                string agentPath = Path.Combine(installDir, AgentExe);
                RunAndWait(agentPath, "--register", 10000);
                CreateOfflineShortcut(installDir, agentPath);
                Process.Start(new ProcessStartInfo { FileName = agentPath, Arguments = silent ? "" : "--offline", WorkingDirectory = installDir, UseShellExecute = true });

                if (!silent)
                {
                    string runtimeWarning = HasVpdlRuntime() ? "" : "\r\n\r\n참고: Cognex VPDL Runtime은 라이선스 제품이므로 설치 패키지에 포함되지 않습니다. 이 PC에 VPDL이 설치되어 있어야 시뮬레이션을 실행할 수 있습니다.";
                    MessageBox.Show("VisionQC Agent v" + ProductVersion + " 설치가 완료되었습니다.\r\n프로토콜 등록과 오프라인 UI 실행도 자동으로 처리했습니다.\r\n\r\n설치 위치: " + installDir + runtimeWarning,
                        "VisionQC 설치 완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                return 0;
            }
            catch (Exception ex)
            {
                if (!silent) MessageBox.Show("VisionQC Agent 설치 중 오류가 발생했습니다.\r\n\r\n" + ex.Message, "VisionQC 설치 오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
                else try { File.WriteAllText(silentErrorLog, ex.ToString(), Encoding.UTF8); } catch { }
                return 1;
            }
        }

        private static void ExtractPayload(string installDir)
        {
            string root = Path.GetFullPath(installDir);
            Directory.CreateDirectory(root);
            Assembly assembly = Assembly.GetExecutingAssembly();
            foreach (PayloadFile item in Payload)
            {
                string destination = Path.GetFullPath(Path.Combine(root, item.RelativePath));
                if (!destination.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("잘못된 설치 경로입니다.");
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                using (Stream source = assembly.GetManifestResourceStream(item.ResourceName))
                {
                    if (source == null) throw new FileNotFoundException("설치 리소스를 찾을 수 없습니다.", item.ResourceName);
                    using (FileStream target = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None)) source.CopyTo(target);
                }
            }
            ExtractVpdlWorkerBundle(root);
        }

        private static void ExtractVpdlWorkerBundle(string installDir)
        {
            string root = Path.GetFullPath(installDir);
            string workerRoot = Path.Combine(root, "Workers");
            string bundle = Path.Combine(root, "vpdl-workers.zip");
            if (!File.Exists(bundle)) throw new FileNotFoundException("VPDL Worker 묶음을 찾지 못했습니다.", bundle);
            if (Directory.Exists(workerRoot)) Directory.Delete(workerRoot, true);
            Directory.CreateDirectory(workerRoot);
            using (ZipArchive archive = ZipFile.OpenRead(bundle))
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                string relative = (entry.FullName ?? "").Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
                if (string.IsNullOrWhiteSpace(relative)) continue;
                string target = Path.GetFullPath(Path.Combine(workerRoot, relative));
                if (!target.StartsWith(workerRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("잘못된 Worker 압축 경로입니다.");
                if (string.IsNullOrEmpty(entry.Name)) { Directory.CreateDirectory(target); continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                entry.ExtractToFile(target, true);
            }
            try { File.Delete(bundle); } catch { }
        }

        private static void StopRunningAgent(string installedAgentPath)
        {
            try
            {
                var request = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:17891/api/agent/exit");
                request.Method = "POST";
                request.ContentType = "application/json";
                byte[] body = Encoding.UTF8.GetBytes("{}");
                request.ContentLength = body.Length;
                using (Stream output = request.GetRequestStream()) output.Write(body, 0, body.Length);
                using (var response = (HttpWebResponse)request.GetResponse()) { }
            }
            catch { }
            // 포트가 먼저 닫혀도 Agent EXE는 종료 정리 중일 수 있습니다.
            // 그 상태에서 덮어쓰면 파일 잠금으로 설치가 실패하므로 설치 대상 프로세스가
            // 실제로 끝날 때까지 기다립니다.
            for (int attempt = 0; attempt < 60; attempt++)
            {
                if (!IsInstalledAgentRunning(installedAgentPath)) return;
                Thread.Sleep(250);
            }
            throw new InvalidOperationException("기존 VisionQC Agent가 종료되지 않아 설치를 계속할 수 없습니다. 실행 중인 Agent를 종료한 뒤 다시 시도해 주세요.");
        }

        private static bool IsInstalledAgentRunning(string installedAgentPath)
        {
            string expected = Path.GetFullPath(installedAgentPath ?? "");
            string installRoot = Path.GetDirectoryName(expected) ?? "";
            string installPrefix = installRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            foreach (string name in new[] { Path.GetFileNameWithoutExtension(AgentExe), "VisionQC.VpdlWorker" })
            foreach (Process process in Process.GetProcessesByName(name))
            {
                try
                {
                    string processPath = process.MainModule == null ? "" : process.MainModule.FileName;
                    string fullPath = string.IsNullOrWhiteSpace(processPath) ? "" : Path.GetFullPath(processPath);
                    if (string.Equals(fullPath, expected, StringComparison.OrdinalIgnoreCase) || fullPath.StartsWith(installPrefix, StringComparison.OrdinalIgnoreCase)) return true;
                }
                catch { }
                finally { try { process.Dispose(); } catch { } }
            }
            return false;
        }

        private static bool IsLoopbackAgentPortOpen()
        {
            try
            {
                using (var client = new TcpClient())
                {
                    IAsyncResult result = client.BeginConnect(IPAddress.Loopback, 17891, null, null);
                    if (!result.AsyncWaitHandle.WaitOne(200)) return false;
                    client.EndConnect(result);
                    return true;
                }
            }
            catch { return false; }
        }

        private static void RunAndWait(string fileName, string arguments, int timeoutMs)
        {
            using (Process process = Process.Start(new ProcessStartInfo { FileName = fileName, Arguments = arguments, WorkingDirectory = Path.GetDirectoryName(fileName), UseShellExecute = false }))
            {
                if (process == null || !process.WaitForExit(timeoutMs)) throw new InvalidOperationException("Agent 프로토콜 등록 시간이 초과되었습니다.");
                if (process.ExitCode != 0) throw new InvalidOperationException("Agent 프로토콜 등록에 실패했습니다. (종료 코드 " + process.ExitCode + ")");
            }
        }

        private static void CreateOfflineShortcut(string installDir, string agentPath)
        {
            try
            {
                string shortcutPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "VisionQC 오프라인 실행.lnk");
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) return;
                dynamic shell = Activator.CreateInstance(shellType);
                dynamic shortcut = shell.CreateShortcut(shortcutPath);
                shortcut.TargetPath = agentPath;
                shortcut.Arguments = "--offline";
                shortcut.WorkingDirectory = installDir;
                shortcut.IconLocation = agentPath + ",0";
                shortcut.Description = "VisionQC 오프라인 UI 실행";
                shortcut.Save();
            }
            catch { }
        }

        private static bool HasVpdlRuntime()
        {
            string root = Environment.GetEnvironmentVariable("COGNEX_VPDL_ROOT");
            if (string.IsNullOrWhiteSpace(root)) root = @"C:\Program Files\Cognex\VisionPro Deep Learning";
            if (!Directory.Exists(root)) return false;
            foreach (string versionRoot in Directory.GetDirectories(root))
            {
                string studio = Path.Combine(versionRoot, "Cognex Deep Learning Studio");
                string managed = Path.Combine(studio, "ViDi.NET.Local.dll");
                if (!File.Exists(managed)) continue;
                try
                {
                    Version api = AssemblyName.GetAssemblyName(managed).Version;
                    string native = Path.Combine(versionRoot, "bin", "vidi_" + api.Major + api.Minor + ".dll");
                    if (File.Exists(native)) return true;
                }
                catch { }
            }
            return false;
        }

        private sealed class PayloadFile
        {
            public readonly string ResourceName;
            public readonly string RelativePath;
            public PayloadFile(string resourceName, string relativePath) { ResourceName = resourceName; RelativePath = relativePath; }
        }
    }
}
