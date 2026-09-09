using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VpdlGreenHeatmapOverlay;

namespace VisionQC.LocalAgent.Services
{
    internal static class GreenProcessHost
    {
        internal static ProcessSummary Run(AppConfig config, IProgress<ProcessProgress> progress,
            CancellationToken token, string runnerPath, string agentHome, string originalPath)
        {
            if (!File.Exists(runnerPath)) throw new FileNotFoundException("독립 Green 실행 파일이 없습니다. Agent를 다시 설치하세요.", runnerPath);
            string runId = Guid.NewGuid().ToString("N");
            string runDirectory = Path.Combine(agentHome, "logs", "green-runs", runId);
            Directory.CreateDirectory(runDirectory);
            string requestPath = Path.Combine(runDirectory, "request.json");
            var json = GreenProcessMessage.Serializer();
            config.OriginalExecution = true;
            File.WriteAllText(requestPath, json.Serialize(config), new UTF8Encoding(false));
            string pipeName = "VisionQC.Green." + runId;
            var security = new PipeSecurity();
            security.SetAccessRuleProtection(true, false);
            security.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User, PipeAccessRights.FullControl, AccessControlType.Allow));
            using (var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 65536, 65536, security))
            using (var child = new Process())
            {
                child.StartInfo = new ProcessStartInfo {
                    FileName = Path.GetFullPath(runnerPath),
                    Arguments = "--request \"" + requestPath + "\" --pipe " + pipeName,
                    WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(runnerPath)),
                    UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                    RedirectStandardOutput = true, RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.Default, StandardErrorEncoding = Encoding.Default
                };
                child.StartInfo.EnvironmentVariables["VISIONQC_AGENT_HOME"] = agentHome;
                if (originalPath != null)
                {
                    child.StartInfo.EnvironmentVariables["PATH"] = originalPath;
                    child.StartInfo.EnvironmentVariables["VISIONQC_ORIGINAL_PATH"] = originalPath;
                }
                child.OutputDataReceived += (sender, e) => { if (e.Data != null) AgentDiagnostics.Write("GREEN_CHILD_STDOUT", Limit(e.Data)); };
                child.ErrorDataReceived += (sender, e) => { if (e.Data != null) AgentDiagnostics.Write("GREEN_CHILD_STDERR", Limit(e.Data)); };
                bool started = false;
                try
                {
                    token.ThrowIfCancellationRequested();
                    var connected = pipe.WaitForConnectionAsync();
                    if (!child.Start()) throw new InvalidOperationException("독립 Green 프로세스를 시작하지 못했습니다.");
                    started = true;
                    child.BeginOutputReadLine(); child.BeginErrorReadLine();
                    AgentDiagnostics.Write("GREEN_CHILD_START", "ParentPID=" + Process.GetCurrentProcess().Id + " | ChildPID=" + child.Id + " | Request=" + requestPath);
                    progress?.Report(new ProcessProgress { Message = "[ORIGINAL] 독립 Green 프로세스 시작 | PID=" + child.Id + " | 원본 Runtime 생성·검사·정리 방식" });
                    var startup = Stopwatch.StartNew();
                    while (!connected.Wait(100))
                    {
                        token.ThrowIfCancellationRequested();
                        if (child.HasExited) throw ExitFailure(child);
                        if (startup.ElapsedMilliseconds > 30000) throw new TimeoutException("독립 Green 프로세스 연결 제한 시간 초과 (30초)");
                    }
                    using (var reader = new StreamReader(pipe, Encoding.UTF8, false, 65536, true))
                    using (var commands = new PipeCommandWriter(pipe))
                    {
                        GreenProcessResult result = null;
                        string failure = null;
                        bool cancelled = false, ready = false, cancelSent = false, terminal = false;
                        var cancelling = new Stopwatch();
                        var exited = new Stopwatch();
                        Task<string> line = reader.ReadLineAsync();
                        while (true)
                        {
                            if (token.IsCancellationRequested && !cancelSent)
                            {
                                cancelSent = true; cancelling.Start();
                                try { commands.WriteLine("cancel"); } catch (IOException) { }
                            }
                            if (cancelSent && cancelling.ElapsedMilliseconds > 15000 && !child.HasExited)
                            {
                                // Terminate only our own isolated child, never other VPDL processes.
                                child.Kill();
                                AgentDiagnostics.Write("GREEN_CHILD_CANCEL", "Cooperative stop exceeded 15 seconds; isolated child terminated | PID=" + child.Id);
                            }
                            // Task.Wait throws AggregateException on a broken pipe; observe the
                            // original I/O exception and retain the child's native exit code below.
                            if (!line.IsCompleted) Thread.Sleep(100);
                            if (line.IsCompleted)
                            {
                                string value = line.GetAwaiter().GetResult();
                                if (value == null) break;
                                var message = json.Deserialize<GreenProcessMessage>(value);
                                if (message == null) throw new InvalidDataException("Empty Green pipe message");
                                if (terminal) throw new InvalidDataException("Message after terminal Green result");
                                switch (message.Type)
                                {
                                    case "ready":
                                        if (message.Pid != child.Id || ready) throw new InvalidDataException("Green runner PID mismatch");
                                        ready = true; break;
                                    case "progress":
                                        if (!ready || message.Progress == null) throw new InvalidDataException("Invalid Green progress");
                                        progress?.Report(message.Progress); break;
                                    case "completed":
                                        if (!ready || message.Result == null) throw new InvalidDataException("Invalid Green result");
                                        result = message.Result; terminal = true; break;
                                    case "failed": failure = message.Error ?? "Green runner failed"; terminal = true; break;
                                    case "cancelled": cancelled = true; terminal = true; break;
                                    default: throw new InvalidDataException("Unknown Green message: " + message.Type);
                                }
                                line = reader.ReadLineAsync();
                            }
                            else if (child.HasExited)
                            {
                                // Drain buffered messages even after process exit, including
                                // the final SDK exception. IsConnected alone loses these.
                                if (!exited.IsRunning) exited.Start();
                                if (exited.ElapsedMilliseconds > 10000) throw ExitFailure(child);
                            }
                        }
                        if (!child.WaitForExit(10000)) throw new TimeoutException("검사 결과 전달 후 독립 Green 프로세스 종료 지연");
                        child.WaitForExit(); // flush async diagnostic output after observed exit
                        AgentDiagnostics.Write("GREEN_CHILD_EXIT", "PID=" + child.Id + " | Exit=" + child.ExitCode);
                        token.ThrowIfCancellationRequested();
                        if (cancelled) throw new OperationCanceledException("독립 Green 검사 중지");
                        if (failure != null) throw new InvalidOperationException(failure);
                        if (child.ExitCode != 0 || !ready || result == null) throw ExitFailure(child);
                        return result.ToSummary();
                    }
                }
                catch (Exception) when (token.IsCancellationRequested) { throw new OperationCanceledException(token); }
                catch (IOException) when (started && child.WaitForExit(1000)) { throw ExitFailure(child); }
                finally
                {
                    // Broken callbacks/pipe errors cannot leave an orphan inference running.
                    try { if (started && !child.HasExited) { child.Kill(); child.WaitForExit(5000); } } catch { }
                }
            }
        }

        private static Exception ExitFailure(Process child)
        {
            string code = child.HasExited ? child.ExitCode + " (0x" + unchecked((uint)child.ExitCode).ToString("X8") + ")" : "unknown";
            return new InvalidOperationException("독립 Green 프로세스가 정상 결과 없이 종료되었습니다. Exit=" + code
                + " | logs/last-sdk-stage.txt, last-green-runner-failure.txt, agent-green-runner-*.log 확인");
        }

        private static string Limit(string line) { return line.Length > 16384 ? line.Substring(0, 16384) : line; }

        private sealed class PipeCommandWriter : StreamWriter
        {
            internal PipeCommandWriter(Stream pipe) : base(pipe, new UTF8Encoding(false), 4096, true) { AutoFlush = true; }
            protected override void Dispose(bool disposing)
            {
                // EOF is expected after the child exits. A final flush must not
                // replace its result or original SDK exception with "pipe broken".
                try { base.Dispose(disposing); } catch (IOException) { }
            }
        }
    }
}
