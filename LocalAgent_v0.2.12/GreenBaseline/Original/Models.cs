using System;
using System.Collections.Generic;
using System.Drawing;

namespace VpdlGreenHeatmapOverlay
{
    internal class AppConfig
    {
        public List<WorkspaceSlotConfig> WorkspaceSlots { get; set; } = new List<WorkspaceSlotConfig>();
        public string OutputRoot { get; set; }
        public string CellIdCsvPath { get; set; }
        public bool KeywordMode { get; set; } = false;
        public string KeywordInputRoot { get; set; }

        public long JpegQuality { get; set; } = 80L;
        public float HeatmapAlpha { get; set; } = 0.55f;
        public bool HeatmapImageSave { get; set; } = true;
        public bool KeepSubfolders { get; set; } = false;
        public bool UseGpu { get; set; } = true;
        public List<int> GpuDevices { get; set; } = new List<int> { 0 };
        public byte HeatmapAlphaCut { get; set; } = 25;
        public bool ForceJetWhenGrayscale { get; set; } = true;
        public int PrintEvery { get; set; } = 100;

        public List<ToolRoiConfig> Tools { get; set; } = new List<ToolRoiConfig>();
        public List<JudgementConfig> Judgements { get; set; } = new List<JudgementConfig>();
    }

    internal class WorkspaceSlotConfig
    {
        public string Key { get; set; }
        public string DisplayName { get; set; }
        public bool Enabled { get; set; }
        public string WorkspacePath { get; set; }
        public string InputRoot { get; set; }
        public string StreamName { get; set; }
        public string Keyword { get; set; }
        public string Electrode { get; set; }
        public string Side { get; set; }
    }

    internal class JudgementConfig
    {
        public int Priority { get; set; }
        public string Name { get; set; }

        public static List<JudgementConfig> CreateDefault()
        {
            return new List<JudgementConfig>
            {
                new JudgementConfig { Priority = 1, Name = "Crack" },
                new JudgementConfig { Priority = 2, Name = "Damage" },
                new JudgementConfig { Priority = 3, Name = "Scrap" },
                new JudgementConfig { Priority = 99, Name = "ERROR" },
            };
        }
    }

    internal class ToolRoiConfig
    {
        public string ToolName { get; set; }
        public Rectangle Roi { get; set; }
        public double? NgScoreThreshold { get; set; }
        public string JudgementName { get; set; }
        public bool UseCaTop { get; set; }
        public bool UseCaBot { get; set; }
        public bool UseAnTop { get; set; }
        public bool UseAnBot { get; set; }

        public bool AppliesTo(string slotKey)
        {
            if (string.Equals(slotKey, "CA_TOP", StringComparison.OrdinalIgnoreCase)) return UseCaTop;
            if (string.Equals(slotKey, "CA_BOT", StringComparison.OrdinalIgnoreCase)) return UseCaBot;
            if (string.Equals(slotKey, "AN_TOP", StringComparison.OrdinalIgnoreCase)) return UseAnTop;
            if (string.Equals(slotKey, "AN_BOT", StringComparison.OrdinalIgnoreCase)) return UseAnBot;
            return false;
        }

        public static List<ToolRoiConfig> CreateDefault()
        {
            return new List<ToolRoiConfig>
            {
                NewDefault("Crack",       450, 670, 1558, 250, 0.5, "Crack"),
                NewDefault("Crack2",      450, 670, 1558, 250, 0.5, "Crack"),
                NewDefault("FoilDamage",  400, 670, 1658, 350, 0.5, "Damage"),
                NewDefault("FoilDamage2", 400, 670, 1658, 350, 0.5, "Damage"),
                NewDefault("FoilDamage3", 400, 670, 1658, 350, 0.5, "Damage"),
                NewDefault("ETC",         400, 570, 1658, 589, 0.5, "Scrap"),
                NewDefault("Separator",   400, 570, 1658, 220, 0.5, "Scrap"),
                NewDefault("Welding",     500, 850, 1450, 170, 0.5, "Scrap"),
                NewDefault("Welding2",    500, 850, 1450, 170, 0.5, "Scrap"),
                NewDefault("Trimming",    450, 950, 1558, 100, 0.6, "Scrap"),
                NewDefault("Trimming2",   450, 950, 1558, 100, 0.5, "Scrap"),
                NewDefault("Trimming3",   400, 940, 1658, 200, 0.5, "Scrap"),
                NewDefault("Trimming4",   400, 940, 1658, 200, 0.5, "Scrap"),
                NewDefault("SideEdge",    1760, 880, 150, 150, 0.5, "Scrap"),
                NewDefault("SideEdge2",   1760, 880, 150, 150, 0.5, "Scrap"),
            };
        }

        private static ToolRoiConfig NewDefault(string name, int x, int y, int w, int h, double threshold, string judgement)
        {
            return new ToolRoiConfig
            {
                ToolName = name,
                Roi = new Rectangle(x, y, w, h),
                NgScoreThreshold = threshold,
                JudgementName = judgement,
                UseCaTop = true,
                UseCaBot = true,
                UseAnTop = true,
                UseAnBot = true
            };
        }
    }

    internal class ProcessProgress
    {
        public string Message { get; set; }
        public int? Processed { get; set; }
        public int? Total { get; set; }
    }

    internal class ProcessSummary
    {
        public int TotalImages { get; set; }
        public int TotalOkCount { get; set; }
        public int TotalNgCount { get; set; }
        public int FilterCellIdCount { get; set; }
        public int SkippedByCellIdCount { get; set; }
        public Dictionary<string, int> NgCountByTool { get; set; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> CountByJudgement { get; set; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public string CsvPath { get; set; }
        public string CellPositionSummaryCsvPath { get; set; }
        public Dictionary<string, string> SlotCsvPaths { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public string OutputRoot { get; set; }
        public TimeSpan Elapsed { get; set; }
    }
}
