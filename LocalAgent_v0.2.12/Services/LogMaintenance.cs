using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;

namespace VisionQC.LocalAgent.Services
{
    // Only application-owned diagnostics are eligible. Result folders and databases are never scanned.
    internal sealed class LogMaintenance
    {
        internal sealed class Settings
        {
            public bool automatic { get; set; } = true;
            public int retentionDays { get; set; } = 30;
            public int maxMegabytes { get; set; } = 1024;
        }
        private sealed class Entry
        {
            internal string Path, Category;
            internal long Size;
            internal DateTime Written;
        }
        private readonly string _root, _settingsPath;
        private readonly object _sync = new object();
        private readonly Func<bool> _workWaiting;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer();
        private Settings _settings;
        private List<Entry> _plan;
        private string _token;
        private DateTime _expires, _nextAutomatic = DateTime.UtcNow.AddMinutes(1);
        private DateTime? _lastCleanup;
        private long _lastFreed;

        internal LogMaintenance(string agentHome, Func<bool> workWaiting = null)
        {
            string home = System.IO.Path.GetFullPath(agentHome);
            _root = System.IO.Path.Combine(home, "logs");
            _settingsPath = System.IO.Path.Combine(home, "data", "log-maintenance.json");
            _settings = new Settings();
            _workWaiting = workWaiting ?? (() => false);
            try { if (File.Exists(_settingsPath)) _settings = Validate(_json.Deserialize<Settings>(File.ReadAllText(_settingsPath))); }
            catch { _settings = new Settings(); }
        }
        private static Settings Validate(Settings value)
        {
            if (value == null || value.retentionDays < 1 || value.retentionDays > 3650 || value.maxMegabytes < 64 || value.maxMegabytes > 102400)
                throw new InvalidDataException("보관 기간은 1~3650일, 용량 한도는 64~102400MB로 입력하세요.");
            return value;
        }
        internal object Status(bool busy = false)
        { lock (_sync) return new { ok = true, settings = _settings, busy, lastCleanup = _lastCleanup, lastFreedBytes = _lastFreed }; }
        internal object Configure(string body)
        {
            var settings = Validate(_json.Deserialize<Settings>(body ?? "{}"));
            lock (_sync)
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_settingsPath));
                string stage = _settingsPath + ".tmp";
                File.WriteAllText(stage, _json.Serialize(settings));
                if (File.Exists(_settingsPath)) File.Replace(stage, _settingsPath, null); else File.Move(stage, _settingsPath);
                _settings = settings; _plan = null; _token = null;
                _nextAutomatic = DateTime.UtcNow.AddMinutes(1);
                return Status();
            }
        }
        private bool SafePath(string path)
        {
            string full = System.IO.Path.GetFullPath(path), root = System.IO.Path.GetFullPath(_root);
            if (!full.StartsWith(root + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return false;
            for (string cursor = full; cursor != null; cursor = System.IO.Path.GetDirectoryName(cursor))
            {
                if ((File.Exists(cursor) || Directory.Exists(cursor)) && (File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0) return false;
                if (string.Equals(cursor, root, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }
        private static bool DiagnosticName(string name)
        {
            string extension = System.IO.Path.GetExtension(name).ToLowerInvariant();
            return extension == ".log" || extension == ".txt" || extension == ".trace" || extension == ".dmp" || extension == ".tmp" ||
                (extension == ".previous" && name.EndsWith(".log.previous", StringComparison.OrdinalIgnoreCase));
        }
        private static bool RunName(string name, bool failure)
        {
            Guid id;
            if (!failure) return Guid.TryParseExact(name, "N", out id);
            DateTime time;
            return name.Length == 56 && name[23] == '-' && Guid.TryParseExact(name.Substring(24), "N", out id) &&
                DateTime.TryParseExact(name.Substring(0, 23), "yyyyMMdd-HHmmss-fffffff", System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out time);
        }
        private void Collect(string directory, string category, List<Entry> entries, bool recurse = false)
        {
            if (!Directory.Exists(directory) || !SafePath(directory + System.IO.Path.DirectorySeparatorChar + ".probe")) return;
            foreach (string path in Directory.EnumerateFiles(directory))
            {
                if (_workWaiting()) throw new OperationCanceledException("검사·분석 요청으로 로그 정리를 미뤘습니다.");
                if (!SafePath(path)) continue;
                string name = System.IO.Path.GetFileName(path);
                bool allowed = DiagnosticName(name) || ((category == "green-runs" || category == "runtime-failures") && (name == "request.json" || name == "result.json"));
                if (category == "agent") allowed = (name.StartsWith("agent-", StringComparison.OrdinalIgnoreCase) && DiagnosticName(name)) ||
                    (name.StartsWith("last-", StringComparison.OrdinalIgnoreCase) && (name.EndsWith(".txt") || name == "last-simulation-request.json")) || name == "cognex-sdk-log-locations.txt";
                if (!allowed) continue;
                try { var info = new FileInfo(path); entries.Add(new Entry { Path = path, Category = category, Size = info.Length, Written = info.LastWriteTimeUtc }); }
                catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
            if (recurse) foreach (string child in Directory.EnumerateDirectories(directory)) Collect(child, category, entries, true);
        }
        private static bool ActiveAgentLog(string path)
        {
            string name = System.IO.Path.GetFileName(path);
            if (!name.StartsWith("agent-", StringComparison.OrdinalIgnoreCase)) return false;
            var match = System.Text.RegularExpressions.Regex.Match(name, @"^agent-(?:worker|launcher|core|green-runner)-(\d+)\.log(?:\.previous)?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            int pid;
            if (!match.Success || !int.TryParse(match.Groups[1].Value, out pid)) return false;
            try { using (var process = Process.GetProcessById(pid)) return !process.HasExited; }
            catch (ArgumentException) { return false; }
            catch { return true; }
        }
        private bool Available(Entry entry)
        {
            try
            {
                if (!SafePath(entry.Path) || ActiveAgentLog(entry.Path)) return false;
                var info = new FileInfo(entry.Path);
                if (!info.Exists || info.Length != entry.Size || info.LastWriteTimeUtc != entry.Written || info.IsReadOnly) return false;
                using (new FileStream(entry.Path, FileMode.Open, FileAccess.Read, FileShare.None)) return true;
            }
            catch { return false; }
        }
        internal object Preview()
        {
            lock (_sync)
            {
                var entries = new List<Entry>();
                Collect(_root, "agent", entries);
                Collect(System.IO.Path.Combine(_root, "cognex"), "cognex", entries, true);
                foreach (string kind in new[] { "green-runs", "runtime-failures" })
                {
                    string directory = System.IO.Path.Combine(_root, kind);
                    if (!Directory.Exists(directory) || !SafePath(directory + "/.probe")) continue;
                    foreach (string child in Directory.EnumerateDirectories(directory))
                        if (RunName(System.IO.Path.GetFileName(child), kind == "runtime-failures")) Collect(child, kind, entries, true);
                }
                long total = entries.Sum(item => item.Size), remaining = total, cap = (long)_settings.maxMegabytes * 1024 * 1024;
                DateTime cutoff = DateTime.UtcNow.AddDays(-_settings.retentionDays);
                var plan = new List<Entry>();
                foreach (var entry in entries.OrderBy(item => item.Written))
                {
                    if (_workWaiting()) throw new OperationCanceledException("검사·분석 요청으로 로그 정리를 미뤘습니다.");
                    bool expired = entry.Written < cutoff;
                    // Keep recent error evidence when trimming by size. Age policy still expires it.
                    if ((!expired && (remaining <= cap || entry.Category == "runtime-failures")) || !Available(entry)) continue;
                    plan.Add(entry); remaining -= entry.Size;
                }
                _plan = plan; _token = Guid.NewGuid().ToString("N"); _expires = DateTime.UtcNow.AddMinutes(5);
                return new { ok = true, planId = _token, settings = _settings, totalBytes = total, fileCount = entries.Count,
                    reclaimableBytes = plan.Sum(item => item.Size), reclaimableFiles = plan.Count,
                    categories = entries.GroupBy(item => item.Category).Select(group => new { name = group.Key, bytes = group.Sum(item => item.Size), files = group.Count() }).ToArray(),
                    samples = plan.Take(8).Select(item => System.IO.Path.GetFileName(item.Path)).ToArray() };
            }
        }
        internal object Clean(string body)
        {
            lock (_sync)
            {
                var request = _json.Deserialize<Dictionary<string, object>>(body ?? "{}");
                object token;
                if (_plan == null || request == null || !request.TryGetValue("planId", out token) || !string.Equals(Convert.ToString(token), _token, StringComparison.Ordinal) || DateTime.UtcNow > _expires)
                    throw new InvalidOperationException("정리 대상을 다시 조회하세요.");
                int deleted = 0, skipped = 0; long freed = 0; bool interrupted = false;
                foreach (var entry in _plan)
                {
                    if (_workWaiting()) { interrupted = true; skipped += _plan.Count - deleted - skipped; break; }
                    if (!Available(entry)) { skipped++; continue; }
                    try { File.Delete(entry.Path); deleted++; freed += entry.Size; }
                    catch (IOException) { skipped++; } catch (UnauthorizedAccessException) { skipped++; }
                }
                _plan = null; _token = null; _lastCleanup = DateTime.UtcNow; _lastFreed = freed;
                _nextAutomatic = interrupted ? DateTime.UtcNow.AddMinutes(1) : DateTime.UtcNow.AddDays(1);
                return new { ok = true, deletedFiles = deleted, skippedFiles = skipped, freedBytes = freed, settings = _settings };
            }
        }
        internal bool AutomaticDue { get { lock (_sync) return _settings.automatic && DateTime.UtcNow >= _nextAutomatic; } }
        internal void DeferAutomatic() { lock (_sync) _nextAutomatic = DateTime.UtcNow.AddMinutes(1); }
        internal void Automatic()
        {
            lock (_sync)
            {
                if (!AutomaticDue) return;
                _nextAutomatic = DateTime.UtcNow.AddDays(1);
                try { Preview(); Clean(_json.Serialize(new { planId = _token })); }
                catch (OperationCanceledException) { _nextAutomatic = DateTime.UtcNow.AddMinutes(1); }
                catch (Exception error) { AgentDiagnostics.Write("LOG_MAINTENANCE", error.Message); }
            }
        }
    }
}
