using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Text;
using SD = System.Drawing;
using SDI = System.Drawing.Imaging;
using ViDi2;
using Runtime = ViDi2.Runtime;
using LocalRuntime = ViDi2.Runtime.Local;
using LocalImages = ViDi2.Local;

namespace VpdlGreenHeatmapOverlay
{
    internal static class GreenOverlayProcessor
    {
        private sealed class WorkspaceContext
        {
            public WorkspaceSlotConfig Slot { get; set; }
            public Runtime.IWorkspace Workspace { get; set; }
            public Runtime.IStream Stream { get; set; }
            public Dictionary<string, Runtime.ITool> ToolMap { get; set; }
            public List<ToolRoiConfig> Tools { get; set; }
        }

        private sealed class ImageJob
        {
            public string ImagePath { get; set; }
            public string WorkspaceKey { get; set; }
            public string InputRoot { get; set; }
            public string InputRootTag { get; set; }
            public string SlotDisplayName { get; set; }
        }

        private sealed class ToolResult
        {
            public string Decision { get; set; }
            public double Score { get; set; }
        }

        private sealed class ProcessOneResult
        {
            public bool IsTotalOk { get; set; }
            public string Judgement { get; set; }
            public string DateText { get; set; }
            public string TimeText { get; set; }
            public string FileName { get; set; }
            public string FullPath { get; set; }
            public string CellId { get; set; }
            public string Position { get; set; }
            public Dictionary<string, ToolResult> ToolResults { get; set; } = new Dictionary<string, ToolResult>(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class ImageJobListResult
        {
            public List<ImageJob> Jobs { get; set; }
            public int SkippedByCellIdCount { get; set; }
        }

        public static ProcessSummary Run(AppConfig config, IProgress<ProcessProgress> progress, CancellationToken token)
        {
            return Run(config, null, false, progress, token);
        }

        internal static ProcessSummary Run(AppConfig config, LocalRuntime.Control sharedControl, bool reusePreloadedWorkspaces, IProgress<ProcessProgress> progress, CancellationToken token)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (string.IsNullOrWhiteSpace(config.OutputRoot)) throw new DirectoryNotFoundException("출력 폴더가 지정되지 않았습니다.");
            Directory.CreateDirectory(config.OutputRoot);

            string runStamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

            Report(progress, "Cell ID 필터 로드 중...");
            HashSet<string> cellIdFilter = LoadCellIdFilter(config.CellIdCsvPath);
            if (!string.IsNullOrWhiteSpace(config.CellIdCsvPath) && cellIdFilter.Count == 0)
                throw new System.InvalidOperationException("Cell ID CSV를 선택했지만 읽을 수 있는 Cell ID가 0개입니다. CSV의 'Cell ID' 컬럼 또는 첫 번째 컬럼에 J/P로 시작하는 16자리 Cell ID가 있는지 확인하세요.");
            if (cellIdFilter.Count > 0)
                Report(progress, string.Format("Cell ID 필터 로드 완료: {0} IDs", cellIdFilter.Count));

            var enabledSlots = config.WorkspaceSlots.Where(s => s.Enabled).ToList();
            foreach (var slot in enabledSlots)
                Directory.CreateDirectory(GetSlotOutputDir(config, slot));

            Report(progress, "이미지 목록 생성 중...");
            var jobListResult = BuildImageJobs(config, cellIdFilter, token);
            var imageJobs = jobListResult.Jobs;
            Report(progress, string.Format("검사 대상 이미지: {0}개", imageJobs.Count));

            Report(progress, "초기화 중...");
            var gpuMode = config.UseGpu ? GpuMode.SingleDevicePerTool : GpuMode.NoSupport;
            var gpuList = config.UseGpu ? config.GpuDevices : new List<int>();

            var judgementPriority = config.Judgements
                .GroupBy(j => j.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Min(x => x.Priority), StringComparer.OrdinalIgnoreCase);
            if (!judgementPriority.ContainsKey("ERROR")) judgementPriority["ERROR"] = int.MaxValue - 1;

            var countByJudgement = config.Judgements.ToDictionary(j => j.Name, j => 0, StringComparer.OrdinalIgnoreCase);
            if (!countByJudgement.ContainsKey("OK")) countByJudgement["OK"] = 0;
            if (!countByJudgement.ContainsKey("ERROR")) countByJudgement["ERROR"] = 0;

            var ngCountByTool = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var slot in enabledSlots)
            {
                foreach (var toolName in GetDistinctToolNames(config.Tools.Where(t => t.AppliesTo(slot.Key))))
                    ngCountByTool[slot.DisplayName + "/" + toolName] = 0;
            }

            int totalOkCount = 0, totalNgCount = 0;
            string integratedCsvPath = Path.Combine(config.OutputRoot, string.Format("results_{0}.csv", runStamp));
            var slotCsvPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var cellPositionSummary = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            var cellPositionOrder = new List<string>();
            var sw = Stopwatch.StartNew();

            var control = sharedControl ?? new LocalRuntime.Control(gpuMode, gpuList);
            bool ownsControl = sharedControl == null;
            try
            {
                var contexts = LoadWorkspaceContexts(config, control, progress, reusePreloadedWorkspaces);
                var activeSlotToolNames = contexts.ToDictionary(
                    kv => kv.Key,
                    kv => GetDistinctToolNames(kv.Value.Tools),
                    StringComparer.OrdinalIgnoreCase);

                var slotWriters = new Dictionary<string, StreamWriter>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    foreach (var slot in enabledSlots)
                    {
                        string slotCsv = Path.Combine(GetSlotOutputDir(config, slot), string.Format("results_{0}_{1}.csv", SafeFileName(slot.Key), runStamp));
                        slotCsvPaths[slot.DisplayName] = slotCsv;
                        var writer = new StreamWriter(slotCsv, false, new System.Text.UTF8Encoding(true));
                        WriteHeader(activeSlotToolNames[slot.Key], writer);
                        slotWriters[slot.Key] = writer;
                    }

                    using (var integratedCsv = new StreamWriter(integratedCsvPath, false, new System.Text.UTF8Encoding(true)))
                    {
                        WriteIntegratedSummaryHeader(integratedCsv);
                        int idx = 0;
                        foreach (var job in imageJobs)
                        {
                            token.ThrowIfCancellationRequested();
                            idx++;
                            WorkspaceContext context;
                            if (!contexts.TryGetValue(job.WorkspaceKey, out context))
                                throw new System.InvalidOperationException("워크스페이스 Context를 찾을 수 없습니다: " + job.WorkspaceKey);

                            var one = ProcessOneImage(config, context, job, ngCountByTool, judgementPriority, token);
                            if (one.IsTotalOk) totalOkCount++; else totalNgCount++;
                            if (!countByJudgement.ContainsKey(one.Judgement)) countByJudgement[one.Judgement] = 0;
                            countByJudgement[one.Judgement]++;

                            progress?.Report(new ProcessProgress
                            {
                                Message = string.Format("{0}%  {1}/{2} | Total OK={3}, NG={4}", imageJobs.Count > 0 ? (int)Math.Round(idx * 100.0 / imageJobs.Count) : 0, idx, imageJobs.Count, totalOkCount, totalNgCount),
                                Processed = idx, Total = imageJobs.Count, OkCount = totalOkCount, NgCount = totalNgCount, CurrentFile = job.ImagePath,
                                LiveRecord = ToLiveRecord(one)
                            });

                            WriteIntegratedSummaryRow(integratedCsv, one);
                            UpdateCellPositionSummary(cellPositionSummary, cellPositionOrder, one);
                            StreamWriter slotWriter;
                            if (slotWriters.TryGetValue(job.WorkspaceKey, out slotWriter))
                                WriteRow(activeSlotToolNames[job.WorkspaceKey], slotWriter, one);

                            if (idx == 1 || idx % config.PrintEvery == 0 || idx == imageJobs.Count)
                            {
                                var msg = string.Format("{0}%  {1}/{2} | Total OK={3}, NG={4}", imageJobs.Count > 0 ? (int)Math.Round(idx * 100.0 / imageJobs.Count) : 0, idx, imageJobs.Count, totalOkCount, totalNgCount);
                                Report(progress, msg, idx, imageJobs.Count);
                            }
                        }
                    }
                }
                finally
                {
                    foreach (var w in slotWriters.Values) w.Dispose();
                }
            }
            finally
            {
                if (ownsControl) control.Dispose();
            }

            string cellPositionSummaryCsvPath = WriteCellPositionSummaryCsv(config.OutputRoot, runStamp, cellPositionSummary, cellPositionOrder, config.WorkspaceSlots.Where(x => x != null && x.Enabled).Select(x => x.DisplayName));

            return new ProcessSummary
            {
                TotalImages = imageJobs.Count,
                TotalOkCount = totalOkCount,
                TotalNgCount = totalNgCount,
                FilterCellIdCount = cellIdFilter.Count,
                SkippedByCellIdCount = jobListResult.SkippedByCellIdCount,
                NgCountByTool = ngCountByTool,
                CountByJudgement = countByJudgement,
                CsvPath = integratedCsvPath,
                CellPositionSummaryCsvPath = cellPositionSummaryCsvPath,
                SlotCsvPaths = slotCsvPaths,
                OutputRoot = config.OutputRoot,
                Elapsed = sw.Elapsed
            };
        }

        private static Dictionary<string, WorkspaceContext> LoadWorkspaceContexts(AppConfig config, LocalRuntime.Control control, IProgress<ProcessProgress> progress, bool reusePreloadedWorkspaces = false)
        {
            var contexts = new Dictionary<string, WorkspaceContext>(StringComparer.OrdinalIgnoreCase);
            foreach (var slot in config.WorkspaceSlots.Where(s => s.Enabled))
            {
                if (string.IsNullOrWhiteSpace(slot.WorkspacePath) || !File.Exists(slot.WorkspacePath))
                    throw new FileNotFoundException(slot.DisplayName + " 워크스페이스 파일이 없습니다.", slot.WorkspacePath);

                string workspaceName = "ws_" + slot.Key;
                Report(progress, slot.DisplayName + (reusePreloadedWorkspaces ? " 사전 로드 Runtime 연결 중..." : " 워크스페이스 로드 중..."));
                Runtime.IWorkspace workspace = ResolveWorkspace(control, workspaceName, slot.WorkspacePath, reusePreloadedWorkspaces);
                Runtime.IStream stream = workspace.Streams[slot.StreamName];

                var applicableTools = config.Tools.Where(t => t.AppliesTo(slot.Key)).ToList();
                var activeTools = new List<ToolRoiConfig>();
                var toolMap = new Dictionary<string, Runtime.ITool>(StringComparer.OrdinalIgnoreCase);
                var missing = new List<string>();
                foreach (var cfg in applicableTools)
                {
                    Runtime.ITool tool;
                    if (TryGetTool(stream, cfg.ToolName, out tool))
                    {
                        toolMap[cfg.ToolName] = tool;
                        activeTools.Add(cfg);
                    }
                    else
                    {
                        missing.Add(cfg.ToolName);
                    }
                }
                if (missing.Count > 0)
                {
                    string allTools = string.Join(", ", stream.Tools.Cast<Runtime.ITool>().Select(t => t.Name));
                    Report(progress, "[WARN] " + slot.DisplayName + "에서 Workspace에 없는 Tool은 제외합니다: " + string.Join(", ", missing));
                    Report(progress, "[INFO] " + slot.DisplayName + " 현재 Stream Tools: " + allTools);
                }
                if (activeTools.Count == 0)
                    throw new System.InvalidOperationException(slot.DisplayName + "에서 실행 가능한 Tool이 없습니다. Option의 Tool 위치 체크와 Workspace 내 ToolName을 확인하세요.");

                contexts[slot.Key] = new WorkspaceContext { Slot = slot, Workspace = workspace, Stream = stream, ToolMap = toolMap, Tools = activeTools };
                Report(progress, string.Format("{0} 워크스페이스 로드 완료 - 실행 Tool {1}/{2}개", slot.DisplayName, activeTools.Count, applicableTools.Count));
            }
            return contexts;
        }

        private static Runtime.IWorkspace ResolveWorkspace(LocalRuntime.Control control, string workspaceName, string workspacePath, bool reusePreloaded)
        {
            if (!reusePreloaded) return control.Workspaces.Add(workspaceName, workspacePath);
            Runtime.IWorkspace workspace;
            if (RuntimeWorkspaceRegistry.TryGet(control, workspaceName, out workspace)) return workspace;
            throw new System.InvalidOperationException("사전 로드 Runtime을 찾지 못했습니다: " + workspaceName + ". Runtime File Load를 다시 실행하세요.");
        }

        private static List<string> GetDistinctToolNames(IEnumerable<ToolRoiConfig> tools)
        {
            var list = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in tools)
            {
                if (t == null || string.IsNullOrWhiteSpace(t.ToolName)) continue;
                if (seen.Add(t.ToolName)) list.Add(t.ToolName);
            }
            return list;
        }

        private static void WriteHeader(List<string> toolNames, StreamWriter csv)
        {
            var header = new List<string> { "Date", "Time", "FileName", "FullPath", "Cell ID", "Position" };
            foreach (var toolName in toolNames)
            {
                header.Add(toolName + "_result");
                header.Add(toolName + "_score");
            }
            header.Add("total_result");
            header.Add("Judgement");
            csv.WriteLine(string.Join(",", header.Select(EscapeCsv)));
        }

        private static void WriteRow(List<string> toolNames, StreamWriter csv, ProcessOneResult result)
        {
            var row = new List<string> { result.DateText, result.TimeText, result.FileName, result.FullPath, result.CellId, result.Position };
            foreach (var toolName in toolNames)
            {
                ToolResult tr;
                if (result.ToolResults.TryGetValue(toolName, out tr))
                {
                    row.Add(tr.Decision);
                    row.Add(double.IsNaN(tr.Score) ? "" : tr.Score.ToString("0.######"));
                }
            }
            row.Add(result.IsTotalOk ? "OK" : "NG");
            row.Add(result.Judgement);
            csv.WriteLine(string.Join(",", row.Select(EscapeCsv)));
        }

        private static LiveAnalysisRecord ToLiveRecord(ProcessOneResult result)
        {
            var live = new LiveAnalysisRecord
            {
                FileName = result.FileName,
                FullPath = result.FullPath,
                CellId = result.CellId,
                Position = result.Position,
                TotalResult = result.IsTotalOk ? "OK" : "NG",
                Judgement = result.Judgement
            };
            foreach (var pair in result.ToolResults)
            {
                double? score = double.IsNaN(pair.Value.Score) ? (double?)null : pair.Value.Score;
                live.Tools[pair.Key] = new LiveToolResult { Tool = pair.Key, Result = pair.Value.Decision, Score = score };
            }
            return live;
        }

        private static void WriteIntegratedSummaryHeader(StreamWriter csv)
        {
            var header = new List<string>
            {
                "Date", "Time", "FileName", "FullPath", "Cell ID", "Position", "total_result", "Judgement"
            };
            csv.WriteLine(string.Join(",", header.Select(EscapeCsv)));
        }

        private static void WriteIntegratedSummaryRow(StreamWriter csv, ProcessOneResult result)
        {
            var row = new List<string>
            {
                result.DateText,
                result.TimeText,
                result.FileName,
                result.FullPath,
                result.CellId,
                result.Position,
                result.IsTotalOk ? "OK" : "NG",
                result.Judgement
            };
            csv.WriteLine(string.Join(",", row.Select(EscapeCsv)));
        }

        private static void UpdateCellPositionSummary(Dictionary<string, Dictionary<string, string>> summary, List<string> order, ProcessOneResult result)
        {
            if (summary == null || order == null || result == null) return;
            if (string.IsNullOrWhiteSpace(result.CellId)) return;

            string cellId = result.CellId.Trim().ToUpperInvariant();
            string position = NormalizeSummaryPosition(result.Position);
            if (string.IsNullOrWhiteSpace(position)) return;

            Dictionary<string, string> row;
            if (!summary.TryGetValue(cellId, out row))
            {
                row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                summary[cellId] = row;
                order.Add(cellId);
            }

            string current;
            row.TryGetValue(position, out current);
            string next = result.IsTotalOk ? "OK" : "NG";

            // 같은 Cell ID / 같은 Position에 이미지가 여러 장 있을 수 있으므로 NG를 우선한다.
            // 기존 값이 NG이면 유지, 기존 값이 OK이고 새 값이 NG이면 NG로 승격한다.
            if (string.Equals(current, "NG", StringComparison.OrdinalIgnoreCase)) return;
            if (string.Equals(next, "NG", StringComparison.OrdinalIgnoreCase)) row[position] = "NG";
            else if (string.IsNullOrWhiteSpace(current)) row[position] = "OK";
        }

        private static string NormalizeSummaryPosition(string position)
        {
            if (string.IsNullOrWhiteSpace(position)) return "";
            string p = position.Trim();
            if (string.Equals(p, "CA_TOP", StringComparison.OrdinalIgnoreCase) || string.Equals(p, "CA(TOP)", StringComparison.OrdinalIgnoreCase)) return "CA(TOP)";
            if (string.Equals(p, "AN_TOP", StringComparison.OrdinalIgnoreCase) || string.Equals(p, "AN(TOP)", StringComparison.OrdinalIgnoreCase)) return "AN(TOP)";
            if (string.Equals(p, "CA_BOT", StringComparison.OrdinalIgnoreCase) || string.Equals(p, "CA(BOT)", StringComparison.OrdinalIgnoreCase)) return "CA(BOT)";
            if (string.Equals(p, "AN_BOT", StringComparison.OrdinalIgnoreCase) || string.Equals(p, "AN(BOT)", StringComparison.OrdinalIgnoreCase)) return "AN(BOT)";
            return p;
        }

        private static string WriteCellPositionSummaryCsv(string outputRoot, string runStamp, Dictionary<string, Dictionary<string, string>> summary, List<string> order, IEnumerable<string> positionColumns)
        {
            string path = Path.Combine(outputRoot, string.Format("cell_position_summary_{0}.csv", runStamp));
            var columns = (positionColumns ?? Enumerable.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(NormalizeSummaryPosition)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (columns.Count == 0) columns.AddRange(CellPositionSummaryColumns);
            using (var csv = new StreamWriter(path, false, new System.Text.UTF8Encoding(true)))
            {
                var header = new List<string> { "Cell ID" };
                header.AddRange(columns);
                header.Add("total_result");
                csv.WriteLine(string.Join(",", header.Select(EscapeCsv)));

                foreach (string cellId in order)
                {
                    Dictionary<string, string> rowMap;
                    if (!summary.TryGetValue(cellId, out rowMap)) continue;

                    bool hasNg = false;
                    bool hasOk = false;
                    var row = new List<string> { cellId };

                    foreach (string pos in columns)
                    {
                        string value;
                        if (!rowMap.TryGetValue(pos, out value) || string.IsNullOrWhiteSpace(value)) value = "-";
                        if (string.Equals(value, "NG", StringComparison.OrdinalIgnoreCase)) hasNg = true;
                        if (string.Equals(value, "OK", StringComparison.OrdinalIgnoreCase)) hasOk = true;
                        row.Add(value);
                    }

                    string total = hasNg ? "NG" : (hasOk ? "OK" : "-");
                    row.Add(total);
                    csv.WriteLine(string.Join(",", row.Select(EscapeCsv)));
                }
            }
            return path;
        }

        private static ImageJobListResult BuildImageJobs(AppConfig config, HashSet<string> cellIdFilter, CancellationToken token)
        {
            var jobs = new List<ImageJob>();
            var knownImagePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int skippedByCellId = 0;

            foreach (var slot in config.WorkspaceSlots.Where(s => s.Enabled))
            {
                List<string> inputRoots = GetInputRoots(slot);
                for (int rootIndex = 0; rootIndex < inputRoots.Count; rootIndex++)
                {
                    string inputRoot = inputRoots[rootIndex];
                    if (!Directory.Exists(inputRoot)) continue;
                    string inputRootTag = inputRoots.Count > 1 ? BuildInputRootTag(inputRoot, rootIndex) : "";
                    foreach (var path in EnumerateImages(inputRoot))
                    {
                        token.ThrowIfCancellationRequested();
                        if (!knownImagePaths.Add(path)) continue;
                        string fileName = Path.GetFileName(path);
                        if (!FileNameMatchesKeyword(fileName, slot.Keyword))
                            continue;
                        if (cellIdFilter.Count > 0 && !MatchesCellIdFilter(fileName, cellIdFilter))
                        {
                            skippedByCellId++;
                            continue;
                        }
                        jobs.Add(new ImageJob { ImagePath = path, WorkspaceKey = slot.Key, InputRoot = inputRoot, InputRootTag = inputRootTag, SlotDisplayName = slot.DisplayName });
                    }
                }
            }

            jobs.Sort((a, b) =>
            {
                int c = string.Compare(a.WorkspaceKey, b.WorkspaceKey, StringComparison.OrdinalIgnoreCase);
                if (c != 0) return c;
                return string.Compare(a.ImagePath, b.ImagePath, StringComparison.OrdinalIgnoreCase);
            });
            return new ImageJobListResult { Jobs = jobs, SkippedByCellIdCount = skippedByCellId };
        }

        private static List<string> GetInputRoots(WorkspaceSlotConfig slot)
        {
            var roots = new List<string>();
            Action<string> add = value =>
            {
                string path = (value ?? "").Trim();
                if (!string.IsNullOrWhiteSpace(path) && !roots.Any(existing => string.Equals(existing, path, StringComparison.OrdinalIgnoreCase))) roots.Add(path);
            };
            if (slot != null && slot.InputRoots != null) foreach (string root in slot.InputRoots) add(root);
            if (slot != null) add(slot.InputRoot);
            return roots;
        }

        private static string BuildInputRootTag(string inputRoot, int index)
        {
            string name = Path.GetFileName((inputRoot ?? "").TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrWhiteSpace(name)) name = "Root";
            foreach (char invalid in Path.GetInvalidFileNameChars()) name = name.Replace(invalid, '_');
            return string.Format("Source_{0:D2}_{1}", index + 1, name);
        }

        private static bool FileNameMatchesKeyword(string fileName, string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword)) return true;
            return (fileName ?? string.Empty).IndexOf(keyword.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static ProcessOneResult ProcessOneImage(AppConfig config, WorkspaceContext context, ImageJob job, Dictionary<string, int> ngCountByTool, Dictionary<string, int> judgementPriority, CancellationToken token)
        {
            string fileName = Path.GetFileName(job.ImagePath);
            string cellId = ExtractCellId(fileName);
            var judgementCandidates = new List<string>();
            bool allOk = true;
            var result = new ProcessOneResult
            {
                DateText = DateTime.Now.ToString("yyyyMMdd"),
                TimeText = DateTime.Now.ToString("HHmmss"),
                FileName = fileName,
                FullPath = job.ImagePath,
                CellId = cellId,
                Position = context.Slot.DisplayName
            };

            using (var vidiImage = new LocalImages.LibraryImage(job.ImagePath))
            using (ISample sample = context.Stream.CreateSample())
            {
                sample.AddImage(vidiImage);
                SD.Bitmap sourceBitmap = null;
                try
                {
                    foreach (var cfg in context.Tools)
                    {
                        token.ThrowIfCancellationRequested();
                        Runtime.ITool tool = context.ToolMap[cfg.ToolName];
                        sample.Process(tool);
                        string decision = "ERR";
                        double score = double.NaN;
                        SD.Bitmap heatmapBmp = null;
                        IGreenView view = null;
                        var greenMarking = sample.Markings[tool.Name] as IGreenMarking;
                        if (greenMarking != null && greenMarking.Views != null && greenMarking.Views.Count > 0)
                        {
                            view = greenMarking.Views[0];
                            if (view != null && view.BestTag != null)
                            {
                                string bestName = view.BestTag.Name ?? "";
                                score = view.BestTag.Score;
                                decision = bestName.Equals("OK", StringComparison.OrdinalIgnoreCase) ? "OK" : "NG";
                                if (decision == "NG" && !double.IsNaN(score) && cfg.NgScoreThreshold.HasValue && score < cfg.NgScoreThreshold.Value)
                                    decision = "OK";
                            }
                            else decision = "ERR_NO_BESTTAG";

                            if (decision == "NG" && config.HeatmapImageSave)
                            {
                                try
                                {
                                    IImage hm = view.HeatMap;
                                    if (hm != null) heatmapBmp = CloneBitmapFromUnknown(GetPropertyValue(hm, "Bitmap"));
                                }
                                catch { heatmapBmp = null; }
                            }
                        }
                        else decision = "ERR_NO_MARKING";

                        if (!decision.Equals("OK", StringComparison.OrdinalIgnoreCase)) allOk = false;
                        if (decision == "NG")
                        {
                            string ngKey = context.Slot.DisplayName + "/" + cfg.ToolName;
                            if (!ngCountByTool.ContainsKey(ngKey)) ngCountByTool[ngKey] = 0;
                            ngCountByTool[ngKey]++;
                            if (!string.IsNullOrWhiteSpace(cfg.JudgementName)) judgementCandidates.Add(cfg.JudgementName);
                        }
                        else if (decision.StartsWith("ERR", StringComparison.OrdinalIgnoreCase))
                        {
                            judgementCandidates.Add("ERROR");
                        }

                        if (decision == "NG" && config.HeatmapImageSave && heatmapBmp != null)
                        {
                            if (sourceBitmap == null)
                                sourceBitmap = CloneBitmapFromUnknown(GetPropertyValue(vidiImage, "Bitmap"));
                            if (sourceBitmap != null)
                            {
                                SD.Rectangle? overlayRoi = ResolveRuntimeOverlayRoi(tool, view, heatmapBmp, sourceBitmap.Width, sourceBitmap.Height);
                                if (overlayRoi.HasValue)
                                    SaveOverlayImage(config, context.Slot, job, cfg.ToolName, sourceBitmap, heatmapBmp, overlayRoi.Value);
                                else
                                    SaveRoiReadFailDiagnostic(config, context.Slot, job, cfg.ToolName, sourceBitmap, heatmapBmp, tool, view);
                            }
                        }
                        if (heatmapBmp != null) heatmapBmp.Dispose();
                        result.ToolResults[cfg.ToolName] = new ToolResult { Decision = decision, Score = score };
                    }
                }
                finally
                {
                    if (sourceBitmap != null) sourceBitmap.Dispose();
                }
            }

            result.IsTotalOk = allOk;
            result.Judgement = allOk ? "OK" : SelectFinalJudgement(judgementCandidates, judgementPriority);
            return result;
        }

        private static string SelectFinalJudgement(List<string> candidates, Dictionary<string, int> priority)
        {
            if (candidates == null || candidates.Count == 0) return "ERROR";
            string best = null;
            int bestPriority = int.MaxValue;
            foreach (string c in candidates)
            {
                if (string.IsNullOrWhiteSpace(c)) continue;
                int p;
                if (!priority.TryGetValue(c, out p)) p = int.MaxValue - 10;
                if (best == null || p < bestPriority)
                {
                    best = c;
                    bestPriority = p;
                }
            }
            return best ?? "ERROR";
        }

        internal static StreamingSession BeginStreaming(AppConfig config, IProgress<ProcessProgress> progress, CancellationToken token)
        {
            return new StreamingSession(config, progress, token);
        }

        internal static StreamingSession BeginStreaming(AppConfig config, LocalRuntime.Control sharedControl, IProgress<ProcessProgress> progress, CancellationToken token)
        {
            return new StreamingSession(config, sharedControl, false, progress, token);
        }

        internal static StreamingSession BeginStreaming(AppConfig config, LocalRuntime.Control sharedControl, bool reusePreloadedWorkspaces, IProgress<ProcessProgress> progress, CancellationToken token)
        {
            return new StreamingSession(config, sharedControl, reusePreloadedWorkspaces, progress, token);
        }

        internal sealed class StreamingSession : IDisposable
        {
            private readonly AppConfig _config;
            private readonly IProgress<ProcessProgress> _progress;
            private readonly string _runStamp;
            private readonly HashSet<string> _cellIdFilter;
            private readonly LocalRuntime.Control _control;
            private readonly bool _ownsControl;
            private readonly Dictionary<string, WorkspaceContext> _contexts;
            private readonly Dictionary<string, List<string>> _activeSlotToolNames;
            private readonly Dictionary<string, StreamWriter> _slotWriters = new Dictionary<string, StreamWriter>(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<string, string> _slotCsvPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            private readonly StreamWriter _integratedCsv;
            private readonly Dictionary<string, Dictionary<string, string>> _cellPositionSummary = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            private readonly List<string> _cellPositionOrder = new List<string>();
            private readonly Dictionary<string, int> _judgementPriority;
            private readonly Dictionary<string, int> _countByJudgement;
            private readonly Dictionary<string, int> _ngCountByTool;
            private readonly Stopwatch _sw = Stopwatch.StartNew();
            private int _totalImages;
            private int _totalOkCount;
            private int _totalNgCount;
            private int _skippedByCellIdCount;
            private bool _finished;

            internal int TotalOkCount { get { return _totalOkCount; } }
            internal int TotalNgCount { get { return _totalNgCount; } }

            internal StreamingSession(AppConfig config, IProgress<ProcessProgress> progress, CancellationToken token)
                : this(config, null, false, progress, token)
            {
            }

            internal StreamingSession(AppConfig config, LocalRuntime.Control sharedControl, IProgress<ProcessProgress> progress, CancellationToken token)
                : this(config, sharedControl, false, progress, token)
            {
            }

            internal StreamingSession(AppConfig config, LocalRuntime.Control sharedControl, bool reusePreloadedWorkspaces, IProgress<ProcessProgress> progress, CancellationToken token)
            {
                if (config == null) throw new ArgumentNullException(nameof(config));
                if (string.IsNullOrWhiteSpace(config.OutputRoot)) throw new DirectoryNotFoundException("출력 폴더가 지정되지 않았습니다.");
                _config = config;
                _progress = progress;
                _runStamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                Directory.CreateDirectory(_config.OutputRoot);

                Report(progress, "Cell ID 필터 로드 중...");
                _cellIdFilter = LoadCellIdFilter(_config.CellIdCsvPath);
                if (!string.IsNullOrWhiteSpace(_config.CellIdCsvPath) && _cellIdFilter.Count == 0)
                    throw new System.InvalidOperationException("Cell ID CSV를 선택했지만 읽을 수 있는 Cell ID가 0개입니다. CSV의 'Cell ID' 컬럼 또는 첫 번째 컬럼에 J/P로 시작하는 16자리 Cell ID가 있는지 확인하세요.");
                if (_cellIdFilter.Count > 0)
                    Report(progress, string.Format("Cell ID 필터 로드 완료: {0} IDs", _cellIdFilter.Count));

                var enabledSlots = _config.WorkspaceSlots.Where(s => s.Enabled).ToList();
                foreach (var slot in enabledSlots)
                    Directory.CreateDirectory(GetSlotOutputDir(_config, slot));

                if (sharedControl != null)
                {
                    _control = sharedControl;
                    _ownsControl = false;
                }
                else
                {
                    var gpuMode = _config.UseGpu ? GpuMode.SingleDevicePerTool : GpuMode.NoSupport;
                    var gpuList = _config.UseGpu ? _config.GpuDevices : new List<int>();
                    _control = new LocalRuntime.Control(gpuMode, gpuList);
                    _ownsControl = true;
                }

                _contexts = LoadWorkspaceContexts(_config, _control, progress, reusePreloadedWorkspaces);
                _activeSlotToolNames = _contexts.ToDictionary(
                    kv => kv.Key,
                    kv => GetDistinctToolNames(kv.Value.Tools),
                    StringComparer.OrdinalIgnoreCase);

                _judgementPriority = _config.Judgements
                    .GroupBy(j => j.Name, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.Min(x => x.Priority), StringComparer.OrdinalIgnoreCase);
                if (!_judgementPriority.ContainsKey("ERROR")) _judgementPriority["ERROR"] = int.MaxValue - 1;

                _countByJudgement = _config.Judgements.ToDictionary(j => j.Name, j => 0, StringComparer.OrdinalIgnoreCase);
                if (!_countByJudgement.ContainsKey("OK")) _countByJudgement["OK"] = 0;
                if (!_countByJudgement.ContainsKey("ERROR")) _countByJudgement["ERROR"] = 0;

                _ngCountByTool = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var slot in enabledSlots)
                {
                    foreach (var toolName in GetDistinctToolNames(_config.Tools.Where(t => t.AppliesTo(slot.Key))))
                        _ngCountByTool[slot.DisplayName + "/" + toolName] = 0;
                }

                foreach (var slot in enabledSlots)
                {
                    string slotCsv = Path.Combine(GetSlotOutputDir(_config, slot), string.Format("results_{0}_{1}.csv", SafeFileName(slot.Key), _runStamp));
                    _slotCsvPaths[slot.DisplayName] = slotCsv;
                    var writer = new StreamWriter(slotCsv, false, new System.Text.UTF8Encoding(true));
                    WriteHeader(_activeSlotToolNames[slot.Key], writer);
                    _slotWriters[slot.Key] = writer;
                }

                _integratedCsv = new StreamWriter(Path.Combine(_config.OutputRoot, string.Format("results_{0}.csv", _runStamp)), false, new System.Text.UTF8Encoding(true));
                WriteIntegratedSummaryHeader(_integratedCsv);
            }

            internal bool ProcessImage(string slotKey, string imagePath, string inputRoot, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                string fileName = Path.GetFileName(imagePath);
                WorkspaceSlotConfig slotCfg = null;
                foreach (var s in _config.WorkspaceSlots)
                    if (string.Equals(s.Key, slotKey, StringComparison.OrdinalIgnoreCase)) { slotCfg = s; break; }
                if (slotCfg != null && !FileNameMatchesKeyword(fileName, slotCfg.Keyword))
                    return false;
                if (_cellIdFilter.Count > 0 && !MatchesCellIdFilter(fileName, _cellIdFilter))
                {
                    _skippedByCellIdCount++;
                    return false;
                }

                WorkspaceContext context;
                if (!_contexts.TryGetValue(slotKey, out context))
                    throw new System.InvalidOperationException("워크스페이스 Context를 찾을 수 없습니다: " + slotKey);

                var job = new ImageJob
                {
                    ImagePath = imagePath,
                    WorkspaceKey = slotKey,
                    InputRoot = inputRoot,
                    SlotDisplayName = context.Slot.DisplayName
                };

                var one = ProcessOneImage(_config, context, job, _ngCountByTool, _judgementPriority, token);
                _totalImages++;
                if (one.IsTotalOk) _totalOkCount++; else _totalNgCount++;
                if (!_countByJudgement.ContainsKey(one.Judgement)) _countByJudgement[one.Judgement] = 0;
                _countByJudgement[one.Judgement]++;

                WriteIntegratedSummaryRow(_integratedCsv, one);
                UpdateCellPositionSummary(_cellPositionSummary, _cellPositionOrder, one);
                StreamWriter slotWriter;
                if (_slotWriters.TryGetValue(slotKey, out slotWriter))
                    WriteRow(_activeSlotToolNames[slotKey], slotWriter, one);

                _progress?.Report(new ProcessProgress
                {
                    OkCount = _totalOkCount,
                    NgCount = _totalNgCount,
                    CurrentFile = imagePath,
                    LiveRecord = ToLiveRecord(one)
                });

                return true;
            }

            internal ProcessSummary Finish()
            {
                if (_finished) return BuildSummary();
                _finished = true;
                foreach (var w in _slotWriters.Values) w.Flush();
                _integratedCsv.Flush();
                WriteCellPositionSummaryCsv(_config.OutputRoot, _runStamp, _cellPositionSummary, _cellPositionOrder, _config.WorkspaceSlots.Where(x => x != null && x.Enabled).Select(x => x.DisplayName));
                _sw.Stop();
                return BuildSummary();
            }

            private ProcessSummary BuildSummary()
            {
                return new ProcessSummary
                {
                    TotalImages = _totalImages,
                    TotalOkCount = _totalOkCount,
                    TotalNgCount = _totalNgCount,
                    FilterCellIdCount = _cellIdFilter.Count,
                    SkippedByCellIdCount = _skippedByCellIdCount,
                    NgCountByTool = _ngCountByTool,
                    CountByJudgement = _countByJudgement,
                    CsvPath = Path.Combine(_config.OutputRoot, string.Format("results_{0}.csv", _runStamp)),
                    CellPositionSummaryCsvPath = Path.Combine(_config.OutputRoot, string.Format("cell_position_summary_{0}.csv", _runStamp)),
                    SlotCsvPaths = _slotCsvPaths,
                    OutputRoot = _config.OutputRoot,
                    Elapsed = _sw.Elapsed
                };
            }

            public void Dispose()
            {
                foreach (var w in _slotWriters.Values) w.Dispose();
                if (_integratedCsv != null) _integratedCsv.Dispose();
                if (_ownsControl && _control != null) _control.Dispose();
            }
        }

        public static int CountCellIdFilterForValidation(string csvPath)
        {
            return LoadCellIdFilter(csvPath).Count;
        }

        public static HashSet<string> LoadCellIdFilterForExternalUse(string csvPath)
        {
            return LoadCellIdFilter(csvPath);
        }

        public static bool FileNameMatchesCellIdFilter(string fileName, HashSet<string> filter)
        {
            if (filter == null || filter.Count == 0) return true;
            return MatchesCellIdFilter(fileName, filter);
        }

        private static HashSet<string> LoadCellIdFilter(string csvPath)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(csvPath)) return set;
            if (!File.Exists(csvPath)) return set;

            using (var reader = new StreamReader(csvPath, true))
            {
                string first = null;
                while (!reader.EndOfStream)
                {
                    first = reader.ReadLine();
                    if (!string.IsNullOrWhiteSpace(first)) break;
                }
                if (string.IsNullOrWhiteSpace(first)) return set;

                var firstFields = ParseCsvLine(first);
                int cellIndex = FindCellIdColumnIndex(firstFields);
                bool hasHeader = cellIndex >= 0;
                if (!hasHeader)
                {
                    cellIndex = 0;
                    AddCellId(set, firstFields.Count > 0 ? firstFields[0] : "");
                }

                while (!reader.EndOfStream)
                {
                    string line = reader.ReadLine();
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var fields = ParseCsvLine(line);
                    if (cellIndex >= 0 && cellIndex < fields.Count) AddCellId(set, fields[cellIndex]);
                }
            }
            return set;
        }

        private static int FindCellIdColumnIndex(List<string> fields)
        {
            for (int i = 0; i < fields.Count; i++)
            {
                string n = NormalizeKey(fields[i]);
                if (n == "CELLID" || n.Contains("CELLID")) return i;
            }
            return -1;
        }

        private static List<string> ParseCsvLine(string line)
        {
            var list = new List<string>();
            if (line == null) return list;
            var sb = new System.Text.StringBuilder();
            bool inQuotes = false;
            for (int i = 0; i < line.Length; i++)
            {
                char ch = line[i];
                if (ch == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        sb.Append('"');
                        i++;
                    }
                    else inQuotes = !inQuotes;
                }
                else if (ch == ',' && !inQuotes)
                {
                    list.Add(sb.ToString());
                    sb.Length = 0;
                }
                else sb.Append(ch);
            }
            list.Add(sb.ToString());
            return list;
        }

        private const int CellIdLength = 16;
        private static readonly string[] CellPositionSummaryColumns = { "CA(TOP)", "AN(TOP)", "CA(BOT)", "AN(BOT)" };

        private static void AddCellId(HashSet<string> set, string value)
        {
            string v = NormalizeCellId(value);
            if (v.Length > 0) set.Add(v);
        }

        private static bool MatchesCellIdFilter(string fileName, HashSet<string> filter)
        {
            string cellId = ExtractCellId(fileName);
            return cellId.Length > 0 && filter.Contains(cellId);
        }

        private static IEnumerable<string> ExtractCellIdCandidates(string fileName)
        {
            string cellId = ExtractCellId(fileName);
            if (cellId.Length > 0) yield return cellId;
        }

        private static string NormalizeCellId(string value)
        {
            return ExtractCellId(value);
        }

        private static string CleanCellIdToken(string value)
        {
            if (value == null) return "";
            return new string(value.Trim().Where(c => !char.IsWhiteSpace(c) && c != '"').ToArray());
        }

        private static bool IsCellIdStart(char c)
        {
            return c == 'J' || c == 'P' || c == 'j' || c == 'p';
        }

        private static string NormalizeKey(string value)
        {
            if (value == null) return "";
            return new string(value.Trim().Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        }

        private static object GetPropertyValue(object target, string propertyName)
        {
            if (target == null) return null;
            try
            {
                var prop = target.GetType().GetProperty(propertyName);
                if (prop == null || prop.GetIndexParameters().Length > 0) return null;
                return prop.GetValue(target, null);
            }
            catch
            {
                return null;
            }
        }

        private sealed class RoiCandidate
        {
            public SD.Rectangle Rect;
            public string Path;
            public int Score;
        }

        private static SD.Rectangle? ResolveRuntimeOverlayRoi(Runtime.ITool tool, IGreenView view, SD.Bitmap heatmap, int imageWidth, int imageHeight)
        {
            /*
             * v1.11 핵심 수정
             * ------------------------------------------------------------
             * Heatmap Overlay 위치는 Tool.RegionOfInterest에서 읽지 않는다.
             * VPDL Runtime Probe 결과, Green Tool의 RegionOfInterest는 null일 수 있지만,
             * sample.Process(tool) 이후 생성되는 GreenView에는 실제 검사 View의 좌표가 들어있다.
             *
             * 실제 확인된 API:
             *   view.Pose.OffsetX  -> ROI X
             *   view.Pose.OffsetY  -> ROI Y
             *   view.Size.Width    -> ROI W
             *   view.Size.Height   -> ROI H
             *   view.HeatMap       -> ROI 크기와 같은 Heatmap
             *
             * 따라서 Heatmap은 검사 결과 View 기준 좌표에만 그린다.
             * ROI를 못 읽으면 임의 fallback을 쓰지 않고 diagnostic을 저장한다.
             */
            SD.Rectangle? viewRoi = TryReadRoiFromGreenView(view, heatmap, imageWidth, imageHeight);
            if (viewRoi.HasValue)
                return viewRoi.Value;

            return null;
        }

        private static SD.Rectangle? TryReadRoiFromGreenView(IGreenView view, SD.Bitmap heatmap, int imageWidth, int imageHeight)
        {
            if (view == null) return null;

            object pose = GetPropertyValue(view, "Pose");
            object size = GetPropertyValue(view, "Size");

            double? xVal = TryReadDoubleProperty(pose, "OffsetX");
            double? yVal = TryReadDoubleProperty(pose, "OffsetY");

            // 일부 버전/객체에서 OffsetX/OffsetY property 접근이 실패할 경우 Matrix.ToString()을 파싱한다.
            // 예: "1,0,0,1,450,670" -> 5번째/6번째 값이 X/Y
            if (!xVal.HasValue || !yVal.HasValue)
            {
                double[] matrix = TryParseDoubleList(pose == null ? null : pose.ToString());
                if (matrix != null && matrix.Length >= 6)
                {
                    xVal = matrix[4];
                    yVal = matrix[5];
                }
            }

            if (!xVal.HasValue || !yVal.HasValue)
                return null;

            int w = 0;
            int h = 0;

            // HeatMap Bitmap 크기는 GreenView ROI W/H와 일치하므로 우선 사용한다.
            if (heatmap != null && heatmap.Width > 0 && heatmap.Height > 0)
            {
                w = heatmap.Width;
                h = heatmap.Height;
            }
            else
            {
                double? wVal = TryReadDoubleProperty(size, "Width");
                double? hVal = TryReadDoubleProperty(size, "Height");

                // 예: "1558,250" 형태 대응
                if (!wVal.HasValue || !hVal.HasValue)
                {
                    double[] wh = TryParseDoubleList(size == null ? null : size.ToString());
                    if (wh != null && wh.Length >= 2)
                    {
                        wVal = wh[0];
                        hVal = wh[1];
                    }
                }

                if (!wVal.HasValue || !hVal.HasValue)
                    return null;

                w = (int)Math.Round(wVal.Value);
                h = (int)Math.Round(hVal.Value);
            }

            int x = (int)Math.Round(xVal.Value);
            int y = (int)Math.Round(yVal.Value);

            var roi = new SD.Rectangle(x, y, w, h);
            if (!IsValidRoiInsideImage(roi, imageWidth, imageHeight))
                return null;

            return roi;
        }

        private static double? TryReadDoubleProperty(object obj, string propertyName)
        {
            if (obj == null) return null;
            object value = GetPropertyValue(obj, propertyName);
            if (value == null) return null;

            try
            {
                return Convert.ToDouble(value);
            }
            catch
            {
                return null;
            }
        }

        private static double[] TryParseDoubleList(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            string[] parts = text
                .Replace("Identity", "")
                .Split(new[] { ',', ';', 'x', 'X', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            var values = new List<double>();
            foreach (string raw in parts)
            {
                double v;
                if (double.TryParse(raw.Trim(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out v))
                    values.Add(v);
                else if (double.TryParse(raw.Trim(), out v))
                    values.Add(v);
            }

            return values.Count == 0 ? null : values.ToArray();
        }

        private static void CollectRuntimeRoiCandidates(
            object obj,
            string path,
            int depth,
            HashSet<object> visited,
            List<RoiCandidate> candidates,
            int imageWidth,
            int imageHeight,
            SD.Bitmap heatmap)
        {
            if (obj == null || depth > 5) return;
            Type t = obj.GetType();
            if (IsTerminalType(t)) return;
            if (!t.IsValueType)
            {
                if (visited.Contains(obj)) return;
                visited.Add(obj);
            }

            SD.Rectangle? selfRect = TryConvertToRectangle(obj);
            AddRoiCandidate(selfRect, path, candidates, imageWidth, imageHeight, heatmap);

            var props = t.GetProperties();
            foreach (var prop in props)
            {
                if (prop.GetIndexParameters().Length > 0) continue;

                string propName = prop.Name ?? string.Empty;
                bool roiLikeName = IsRoiLikeName(propName);

                // ROI 후보명이 아니어도 Settings/Parameters/Region 계층 안쪽에 숨어 있을 수 있으므로 일부 컨테이너성 이름은 탐색합니다.
                bool containerLikeName = roiLikeName
                    || propName.IndexOf("Setting", StringComparison.OrdinalIgnoreCase) >= 0
                    || propName.IndexOf("Parameter", StringComparison.OrdinalIgnoreCase) >= 0
                    || propName.IndexOf("Config", StringComparison.OrdinalIgnoreCase) >= 0
                    || propName.IndexOf("Option", StringComparison.OrdinalIgnoreCase) >= 0
                    || propName.IndexOf("Input", StringComparison.OrdinalIgnoreCase) >= 0
                    || propName.IndexOf("View", StringComparison.OrdinalIgnoreCase) >= 0
                    || propName.IndexOf("Image", StringComparison.OrdinalIgnoreCase) >= 0;

                if (!containerLikeName && depth >= 2) continue;

                object value = null;
                try { value = prop.GetValue(obj, null); }
                catch { continue; }
                if (value == null) continue;

                SD.Rectangle? rect = TryConvertToRectangle(value);
                AddRoiCandidate(rect, path + "." + propName, candidates, imageWidth, imageHeight, heatmap);

                if (!IsTerminalType(value.GetType()))
                    CollectRuntimeRoiCandidates(value, path + "." + propName, depth + 1, visited, candidates, imageWidth, imageHeight, heatmap);
            }
        }

        private static bool IsTerminalType(Type t)
        {
            if (t == null) return true;
            if (t.IsPrimitive || t.IsEnum) return true;
            if (t == typeof(string) || t == typeof(decimal) || t == typeof(DateTime)) return true;
            if (typeof(SD.Image).IsAssignableFrom(t)) return true;
            if (typeof(System.IO.Stream).IsAssignableFrom(t)) return true;
            return false;
        }

        private static bool IsRoiLikeName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            return name.IndexOf("ROI", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Roi", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Region", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Area", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Bounds", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Rectangle", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Rect", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Search", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Processing", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void AddRoiCandidate(SD.Rectangle? roi, string path, List<RoiCandidate> candidates, int imageWidth, int imageHeight, SD.Bitmap heatmap)
        {
            if (!IsValidRoiInsideImage(roi, imageWidth, imageHeight)) return;

            SD.Rectangle r = roi.Value;
            if (r.Width == imageWidth && r.Height == imageHeight && r.X == 0 && r.Y == 0)
                return; // 전체 이미지는 검사 ROI로 보지 않습니다.

            int score = 0;
            if (IsRoiLikeName(path)) score += 100;
            if (path.IndexOf("Tool", StringComparison.OrdinalIgnoreCase) >= 0) score += 30;
            if (path.IndexOf("Region", StringComparison.OrdinalIgnoreCase) >= 0) score += 30;
            if (path.IndexOf("ROI", StringComparison.OrdinalIgnoreCase) >= 0 || path.IndexOf("Roi", StringComparison.OrdinalIgnoreCase) >= 0) score += 30;
            if (path.IndexOf("Search", StringComparison.OrdinalIgnoreCase) >= 0 || path.IndexOf("Processing", StringComparison.OrdinalIgnoreCase) >= 0) score += 20;

            if (heatmap != null)
            {
                int dw = Math.Abs(r.Width - heatmap.Width);
                int dh = Math.Abs(r.Height - heatmap.Height);
                if (dw == 0 && dh == 0) score += 80;
                else if (dw <= 4 && dh <= 4) score += 60;
                else if (Math.Abs((double)r.Width / Math.Max(1, r.Height) - (double)heatmap.Width / Math.Max(1, heatmap.Height)) < 0.02) score += 20;
            }

            candidates.Add(new RoiCandidate { Rect = r, Path = path, Score = score });
        }

        private static bool IsValidRoi(SD.Rectangle? roi)
        {
            return roi.HasValue && roi.Value.Width > 0 && roi.Value.Height > 0;
        }

        private static bool IsValidRoiInsideImage(SD.Rectangle? roi, int imageWidth, int imageHeight)
        {
            if (!IsValidRoi(roi)) return false;
            SD.Rectangle r = roi.Value;
            if (r.X < 0 || r.Y < 0) return false;
            if (r.X >= imageWidth || r.Y >= imageHeight) return false;
            if (r.Width > imageWidth || r.Height > imageHeight) return false;
            if (r.X + r.Width > imageWidth) return false;
            if (r.Y + r.Height > imageHeight) return false;
            return true;
        }

        private static SD.Rectangle? TryConvertToRectangle(object roiObj)
        {
            if (roiObj == null) return null;
            if (roiObj is SD.Rectangle) return (SD.Rectangle)roiObj;
            if (roiObj is SD.RectangleF)
            {
                var rf = (SD.RectangleF)roiObj;
                return new SD.Rectangle(ToIntRound(rf.X), ToIntRound(rf.Y), ToIntRound(rf.Width), ToIntRound(rf.Height));
            }

            object xObj = GetFirstPropertyValue(roiObj, "X", "x", "Left", "left", "OriginX", "OriginXInPixels", "OffsetX", "TranslationX");
            object yObj = GetFirstPropertyValue(roiObj, "Y", "y", "Top", "top", "OriginY", "OriginYInPixels", "OffsetY", "TranslationY");
            object wObj = GetFirstPropertyValue(roiObj, "Width", "width", "W", "SizeX", "SizeXInPixels", "SizeWidth");
            object hObj = GetFirstPropertyValue(roiObj, "Height", "height", "H", "SizeY", "SizeYInPixels", "SizeHeight");
            if (xObj != null && yObj != null && wObj != null && hObj != null)
            {
                int x = ToIntRound(xObj), y = ToIntRound(yObj), w = ToIntRound(wObj), h = ToIntRound(hObj);
                if (w > 0 && h > 0) return new SD.Rectangle(x, y, w, h);
            }

            object leftObj = GetFirstPropertyValue(roiObj, "Left", "left", "MinX");
            object topObj = GetFirstPropertyValue(roiObj, "Top", "top", "MinY");
            object rightObj = GetFirstPropertyValue(roiObj, "Right", "right", "MaxX");
            object bottomObj = GetFirstPropertyValue(roiObj, "Bottom", "bottom", "MaxY");
            if (leftObj != null && topObj != null && rightObj != null && bottomObj != null)
            {
                int left = ToIntRound(leftObj), top = ToIntRound(topObj), right = ToIntRound(rightObj), bottom = ToIntRound(bottomObj);
                int w = right - left, h = bottom - top;
                if (w > 0 && h > 0) return new SD.Rectangle(left, top, w, h);
            }

            object cxObj = GetFirstPropertyValue(roiObj, "CenterX", "CentreX");
            object cyObj = GetFirstPropertyValue(roiObj, "CenterY", "CentreY");
            object sxObj = GetFirstPropertyValue(roiObj, "SizeX", "Width");
            object syObj = GetFirstPropertyValue(roiObj, "SizeY", "Height");
            if (cxObj != null && cyObj != null && sxObj != null && syObj != null)
            {
                int cx = ToIntRound(cxObj), cy = ToIntRound(cyObj), w = ToIntRound(sxObj), h = ToIntRound(syObj);
                if (w > 0 && h > 0) return new SD.Rectangle(cx - w / 2, cy - h / 2, w, h);
            }

            return null;
        }

        private static object GetFirstPropertyValue(object target, params string[] propertyNames)
        {
            foreach (string name in propertyNames)
            {
                object value = GetPropertyValue(target, name);
                if (value != null) return value;
            }
            return null;
        }

        private static int ToIntRound(object value)
        {
            return (int)Math.Round(Convert.ToDouble(value));
        }

        private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
        {
            public new bool Equals(object x, object y) { return object.ReferenceEquals(x, y); }
            public int GetHashCode(object obj) { return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj); }
        }

        private static void SaveRoiReadFailDiagnostic(AppConfig config, WorkspaceSlotConfig slot, ImageJob job, string toolName, SD.Bitmap source, SD.Bitmap heatmap, Runtime.ITool tool, IGreenView view)
        {
            string relDir = GetOutputRelativeDirectory(config, job);
            string saveDir = Path.Combine(GetSlotOutputDir(config, slot), toolName, "ROI_READ_FAIL", relDir);
            Directory.CreateDirectory(saveDir);
            string stem = Path.GetFileNameWithoutExtension(job.ImagePath);

            if (heatmap != null)
                heatmap.Save(Path.Combine(saveDir, stem + "_heatmap_raw.png"), SDI.ImageFormat.Png);

            using (var sw = new StreamWriter(Path.Combine(saveDir, stem + "_roi_diagnostic.txt"), false, Encoding.UTF8))
            {
                sw.WriteLine("Runtime ROI를 읽지 못해 Heatmap Overlay를 저장하지 않았습니다.");
                sw.WriteLine("잘못된 위치에 그리지 않기 위해 수동/기본 ROI fallback은 사용하지 않습니다.");
                sw.WriteLine("File=" + job.ImagePath);
                sw.WriteLine("Tool=" + toolName);
                sw.WriteLine("Source=" + source.Width + "x" + source.Height);
                if (heatmap != null) sw.WriteLine("Heatmap=" + heatmap.Width + "x" + heatmap.Height);
                sw.WriteLine();
                DumpPropertyTree(sw, tool, "Tool", 0, new HashSet<object>(new ReferenceEqualityComparer()));
                DumpPropertyTree(sw, view, "View", 0, new HashSet<object>(new ReferenceEqualityComparer()));
            }
        }

        private static void DumpPropertyTree(StreamWriter sw, object obj, string path, int depth, HashSet<object> visited)
        {
            if (obj == null || depth > 3) return;
            Type t = obj.GetType();
            if (IsTerminalType(t)) return;
            if (!t.IsValueType)
            {
                if (visited.Contains(obj)) return;
                visited.Add(obj);
            }
            sw.WriteLine(path + " : " + t.FullName);
            foreach (var prop in t.GetProperties())
            {
                if (prop.GetIndexParameters().Length > 0) continue;
                object value = null;
                try { value = prop.GetValue(obj, null); } catch { continue; }
                if (value == null)
                {
                    sw.WriteLine(path + "." + prop.Name + " = null");
                    continue;
                }
                SD.Rectangle? r = TryConvertToRectangle(value);
                if (r.HasValue)
                    sw.WriteLine(path + "." + prop.Name + " = RECT X=" + r.Value.X + ", Y=" + r.Value.Y + ", W=" + r.Value.Width + ", H=" + r.Value.Height);
                else
                    sw.WriteLine(path + "." + prop.Name + " : " + value.GetType().FullName);

                if (IsRoiLikeName(prop.Name) || prop.Name.IndexOf("Setting", StringComparison.OrdinalIgnoreCase) >= 0 || prop.Name.IndexOf("Parameter", StringComparison.OrdinalIgnoreCase) >= 0)
                    DumpPropertyTree(sw, value, path + "." + prop.Name, depth + 1, visited);
            }
        }

        private static SD.Bitmap CloneBitmapFromUnknown(object foreignBitmap)
        {
            if (foreignBitmap == null) return null;

            var alreadyBitmap = foreignBitmap as SD.Bitmap;
            if (alreadyBitmap != null)
                return (SD.Bitmap)alreadyBitmap.Clone();

            using (var ms = new MemoryStream())
            {
                var foreignType = foreignBitmap.GetType();
                var foreignAsm = foreignType.Assembly;
                var foreignImageFormatType = foreignAsm.GetType("System.Drawing.Imaging.ImageFormat");
                object pngFormat = null;
                if (foreignImageFormatType != null)
                {
                    var pngProp = foreignImageFormatType.GetProperty("Png");
                    if (pngProp != null) pngFormat = pngProp.GetValue(null, null);
                }

                var saveMethod = foreignType.GetMethods()
                    .FirstOrDefault(m =>
                    {
                        if (!string.Equals(m.Name, "Save", StringComparison.Ordinal)) return false;
                        var ps = m.GetParameters();
                        return ps.Length == 2 && typeof(Stream).IsAssignableFrom(ps[0].ParameterType);
                    });

                if (saveMethod == null || pngFormat == null)
                    throw new System.InvalidOperationException("외부 Bitmap 형식을 변환할 수 없습니다.");

                saveMethod.Invoke(foreignBitmap, new object[] { ms, pngFormat });
                ms.Position = 0;
                using (var tmp = new SD.Bitmap(ms))
                    return (SD.Bitmap)tmp.Clone();
            }
        }

        private static void SaveOverlayImage(AppConfig config, WorkspaceSlotConfig slot, ImageJob job, string toolName, SD.Bitmap source, SD.Bitmap heatmap, SD.Rectangle roi)
        {
            var safeRoi = ClampRoi(roi, source.Width, source.Height);
            if (safeRoi.Width <= 0 || safeRoi.Height <= 0) return;

            using (var outBmp = (SD.Bitmap)source.Clone())
            using (var heatResized = new SD.Bitmap(heatmap, new SD.Size(safeRoi.Width, safeRoi.Height)))
            using (var overlay = BuildHeatmapOverlay_KeepColorOrJet_NoUnsafe(heatResized, config.HeatmapAlpha, config.HeatmapAlphaCut, config.ForceJetWhenGrayscale))
            using (var g = SD.Graphics.FromImage(outBmp))
            {
                g.DrawImage(overlay, safeRoi);
                string relDir = GetOutputRelativeDirectory(config, job);
                string saveDir = Path.Combine(GetSlotOutputDir(config, slot), toolName, relDir);
                Directory.CreateDirectory(saveDir);
                string stem = Path.GetFileNameWithoutExtension(job.ImagePath);
                string outPath = Path.Combine(saveDir, stem + ".jpg");
                SaveJpeg(outBmp, outPath, config.JpegQuality);
            }
        }

        private static string GetOutputRelativeDirectory(AppConfig config, ImageJob job)
        {
            string relative = "";
            if (config.KeepSubfolders)
                relative = Path.GetDirectoryName(GetRelativePath(job.InputRoot, job.ImagePath)) ?? "";
            if (string.IsNullOrWhiteSpace(job.InputRootTag)) return relative;
            return string.IsNullOrWhiteSpace(relative) ? job.InputRootTag : Path.Combine(job.InputRootTag, relative);
        }

        private static SD.Bitmap BuildHeatmapOverlay_KeepColorOrJet_NoUnsafe(SD.Bitmap heat, float alphaScale, byte alphaCut, bool forceJetIfGray)
        {
            using (var heat32 = ConvertTo32bppArgb(heat))
            {
                int w = heat32.Width, h = heat32.Height;
                var overlay = new SD.Bitmap(w, h, SDI.PixelFormat.Format32bppArgb);
                var rect = new SD.Rectangle(0, 0, w, h);
                var srcData = heat32.LockBits(rect, SDI.ImageLockMode.ReadOnly, SDI.PixelFormat.Format32bppArgb);
                var dstData = overlay.LockBits(rect, SDI.ImageLockMode.WriteOnly, SDI.PixelFormat.Format32bppArgb);
                try
                {
                    int srcStride = Math.Abs(srcData.Stride), dstStride = Math.Abs(dstData.Stride);
                    byte[] srcBytes = new byte[srcStride * h], dstBytes = new byte[dstStride * h];
                    Marshal.Copy(srcData.Scan0, srcBytes, 0, srcBytes.Length);
                    float aScale = Math.Max(0f, Math.Min(1f, alphaScale));
                    bool isGray = forceJetIfGray ? DetectMostlyGrayscale(srcBytes, srcStride, w, h) : false;
                    for (int y = 0; y < h; y++)
                    {
                        int srcRow = y * srcStride, dstRow = y * dstStride;
                        for (int x = 0; x < w; x++)
                        {
                            int si = srcRow + x * 4;
                            byte b = srcBytes[si], g = srcBytes[si + 1], r = srcBytes[si + 2], a0 = srcBytes[si + 3];
                            int luma = (int)(0.299f * r + 0.587f * g + 0.114f * b);
                            if (luma <= alphaCut)
                            {
                                int di0 = dstRow + x * 4;
                                dstBytes[di0] = 0;
                                dstBytes[di0 + 1] = 0;
                                dstBytes[di0 + 2] = 0;
                                dstBytes[di0 + 3] = 0;
                                continue;
                            }
                            int a = (int)(luma * aScale);
                            if (a > 255) a = 255;
                            if (a0 < 255) a = (a * a0) / 255;
                            if (isGray)
                            {
                                byte jr, jg, jb;
                                JetColor((byte)luma, out jr, out jg, out jb);
                                r = jr; g = jg; b = jb;
                            }
                            int di = dstRow + x * 4;
                            dstBytes[di] = b;
                            dstBytes[di + 1] = g;
                            dstBytes[di + 2] = r;
                            dstBytes[di + 3] = (byte)a;
                        }
                    }
                    Marshal.Copy(dstBytes, 0, dstData.Scan0, dstBytes.Length);
                }
                finally
                {
                    heat32.UnlockBits(srcData);
                    overlay.UnlockBits(dstData);
                }
                return overlay;
            }
        }

        private static bool DetectMostlyGrayscale(byte[] bgra, int stride, int w, int h)
        {
            int samples = 0, grayLike = 0;
            int stepX = Math.Max(1, w / 20), stepY = Math.Max(1, h / 10);
            for (int y = 0; y < h; y += stepY)
            {
                int row = y * stride;
                for (int x = 0; x < w; x += stepX)
                {
                    int i = row + x * 4;
                    byte b = bgra[i], g = bgra[i + 1], r = bgra[i + 2];
                    if (Math.Abs(r - g) <= 3 && Math.Abs(g - b) <= 3 && Math.Abs(r - b) <= 3) grayLike++;
                    samples++;
                    if (samples >= 250) break;
                }
                if (samples >= 250) break;
            }
            return samples > 0 && (grayLike * 100 / samples) >= 90;
        }

        private static void JetColor(byte v, out byte r, out byte g, out byte b)
        {
            float x = v / 255f;
            float rr = Clamp01(1.5f - Math.Abs(4f * x - 3f));
            float gg = Clamp01(1.5f - Math.Abs(4f * x - 2f));
            float bb = Clamp01(1.5f - Math.Abs(4f * x - 1f));
            r = (byte)(rr * 255f);
            g = (byte)(gg * 255f);
            b = (byte)(bb * 255f);
        }

        private static float Clamp01(float v)
        {
            if (v < 0f) return 0f;
            if (v > 1f) return 1f;
            return v;
        }

        private static SD.Bitmap ConvertTo32bppArgb(SD.Bitmap src)
        {
            if (src.PixelFormat == SDI.PixelFormat.Format32bppArgb) return (SD.Bitmap)src.Clone();
            var bmp = new SD.Bitmap(src.Width, src.Height, SDI.PixelFormat.Format32bppArgb);
            using (var g = SD.Graphics.FromImage(bmp)) g.DrawImage(src, new SD.Rectangle(0, 0, src.Width, src.Height));
            return bmp;
        }

        private static void SaveJpeg(SD.Bitmap bmp, string path, long quality)
        {
            var codec = SDI.ImageCodecInfo.GetImageEncoders().FirstOrDefault(c => c.FormatID == SDI.ImageFormat.Jpeg.Guid);
            if (codec == null)
            {
                bmp.Save(path, SDI.ImageFormat.Jpeg);
                return;
            }
            using (var encParams = new SDI.EncoderParameters(1))
            {
                encParams.Param[0] = new SDI.EncoderParameter(SDI.Encoder.Quality, quality);
                bmp.Save(path, codec, encParams);
            }
        }

        private static SD.Rectangle ClampRoi(SD.Rectangle roi, int imgW, int imgH)
        {
            int x = Math.Max(0, roi.X), y = Math.Max(0, roi.Y), w = Math.Max(0, roi.Width), h = Math.Max(0, roi.Height);
            if (x + w > imgW) w = imgW - x;
            if (y + h > imgH) h = imgH - y;
            if (w < 0) w = 0;
            if (h < 0) h = 0;
            return new SD.Rectangle(x, y, w, h);
        }

        private static IEnumerable<string> EnumerateImages(string root)
        {
            var exts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".png", ".bmp", ".jpg", ".jpeg", ".tif", ".tiff" };
            foreach (var p in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
                if (exts.Contains(Path.GetExtension(p))) yield return p;
        }

        private static string ExtractCellId(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return "";

            string stem;
            try
            {
                stem = Path.GetFileNameWithoutExtension(fileName.Trim());
            }
            catch
            {
                stem = fileName.Trim();
            }

            string[] parts = stem.Split('_');
            foreach (string part in parts)
            {
                string token = CleanCellIdToken(part);
                if (token.Length >= CellIdLength && IsCellIdStart(token[0]))
                    return token.Substring(0, CellIdLength).ToUpperInvariant();
            }

            return "";
        }

        private static string EscapeCsv(string s)
        {
            if (s == null) return "";
            if (s.Contains(",") || s.Contains("\"") || s.Contains("\n") || s.Contains("\r")) return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }

        private static string GetRelativePath(string basePath, string fullPath)
        {
            if (!basePath.EndsWith(Path.DirectorySeparatorChar.ToString())) basePath += Path.DirectorySeparatorChar;
            var baseUri = new Uri(basePath);
            var fullUri = new Uri(fullPath);
            var relUri = baseUri.MakeRelativeUri(fullUri);
            return Uri.UnescapeDataString(relUri.ToString()).Replace('/', Path.DirectorySeparatorChar);
        }

        private static string GetSlotOutputDir(AppConfig config, WorkspaceSlotConfig slot)
        {
            return Path.Combine(config.OutputRoot, SafeFileName(slot.DisplayName));
        }

        private static string SafeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "UNKNOWN";
            string s = value.Trim();
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s;
        }

        private static bool TryGetTool(Runtime.IStream stream, string toolName, out Runtime.ITool tool)
        {
            tool = null;
            foreach (Runtime.ITool t in stream.Tools)
                if (t.Name.Equals(toolName, StringComparison.OrdinalIgnoreCase)) { tool = t; return true; }
            return false;
        }

        private static void Report(IProgress<ProcessProgress> progress, string message, int? processed = null, int? total = null)
        {
            if (progress != null) progress.Report(new ProcessProgress { Message = message, Processed = processed, Total = total });
        }
    }
}
