using System;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using VpdlGreenHeatmapOverlay;

namespace VisionQC.LocalAgent.Services
{
    // Local, current-user-only pipe messages. No SDK object crosses the process boundary.
    internal sealed class GreenProcessMessage
    {
        public string Type { get; set; }
        public int Pid { get; set; }
        public ProcessProgress Progress { get; set; }
        public GreenProcessResult Result { get; set; }
        public string Error { get; set; }

        internal static JavaScriptSerializer Serializer()
        {
            return new JavaScriptSerializer { MaxJsonLength = 20 * 1024 * 1024, RecursionLimit = 100 };
        }
    }

    internal sealed class GreenProcessResult
    {
        public int TotalImages { get; set; }
        public int TotalOkCount { get; set; }
        public int TotalNgCount { get; set; }
        public string CsvPath { get; set; }
        public string CellPositionSummaryCsvPath { get; set; }
        public string OutputRoot { get; set; }
        public long ElapsedTicks { get; set; }
        public int FilterCellIdCount { get; set; }
        public int SkippedByCellIdCount { get; set; }
        public Dictionary<string, int> NgCountByTool { get; set; }
        public Dictionary<string, int> CountByJudgement { get; set; }
        public Dictionary<string, string> SlotCsvPaths { get; set; }

        internal static GreenProcessResult From(ProcessSummary summary)
        {
            return new GreenProcessResult { TotalImages = summary.TotalImages, TotalOkCount = summary.TotalOkCount,
                TotalNgCount = summary.TotalNgCount, CsvPath = summary.CsvPath,
                CellPositionSummaryCsvPath = summary.CellPositionSummaryCsvPath, OutputRoot = summary.OutputRoot,
                ElapsedTicks = summary.Elapsed.Ticks, FilterCellIdCount = summary.FilterCellIdCount,
                SkippedByCellIdCount = summary.SkippedByCellIdCount, NgCountByTool = summary.NgCountByTool,
                CountByJudgement = summary.CountByJudgement, SlotCsvPaths = summary.SlotCsvPaths };
        }

        internal ProcessSummary ToSummary()
        {
            return new ProcessSummary { TotalImages = TotalImages, TotalOkCount = TotalOkCount,
                TotalNgCount = TotalNgCount, CsvPath = CsvPath, CellPositionSummaryCsvPath = CellPositionSummaryCsvPath,
                OutputRoot = OutputRoot, Elapsed = TimeSpan.FromTicks(ElapsedTicks),
                FilterCellIdCount = FilterCellIdCount, SkippedByCellIdCount = SkippedByCellIdCount,
                NgCountByTool = Copy(NgCountByTool), CountByJudgement = Copy(CountByJudgement),
                SlotCsvPaths = Copy(SlotCsvPaths) };
        }

        private static Dictionary<string, T> Copy<T>(Dictionary<string, T> values)
        {
            return values == null ? new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, T>(values, StringComparer.OrdinalIgnoreCase);
        }
    }
}
