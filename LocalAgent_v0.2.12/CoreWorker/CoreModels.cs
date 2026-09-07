using System;
using System.Collections.Generic;

namespace VpdlGreenHeatmapOverlay
{
    internal sealed class LiveToolResult
    {
        public string Tool { get; set; }
        public string Result { get; set; }
        public double? Score { get; set; }
        public string OverlayPath { get; set; }
    }

    internal sealed class LiveAnalysisRecord
    {
        public string FileName { get; set; }
        public string FullPath { get; set; }
        public string ProcessingPath { get; set; }
        public string CellId { get; set; }
        public string Position { get; set; }
        public string TotalResult { get; set; }
        public string Judgement { get; set; }
        public Dictionary<string, LiveToolResult> Tools { get; set; } = new Dictionary<string, LiveToolResult>(StringComparer.OrdinalIgnoreCase);
    }
}
