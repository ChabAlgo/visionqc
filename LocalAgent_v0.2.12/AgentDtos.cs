using System;
using System.Collections.Generic;

namespace VisionQC.LocalAgent
{
    public sealed class AgentStartRequest
    {
        public string mode { get; set; }
        public string outputRoot { get; set; }
        public AgentGreenOptions green { get; set; }
        public AgentBlueOptions blue { get; set; }
        public AgentIntegratedOptions integrated { get; set; }
        public List<AgentPositionRequest> positions { get; set; }

        // v0.1.x compatibility fields
        public bool useGpu { get; set; }
        public string gpuDevices { get; set; }
        public int jpegQuality { get; set; }
        public int printEvery { get; set; }
        public bool keepSubfolders { get; set; }
        public bool heatmapImageSave { get; set; }
        public bool keepCropImages { get; set; }
    }

    public sealed class AgentGreenOptions
    {
        public string cellIdCsvPath { get; set; }
        public bool keywordMode { get; set; }
        public string keywordInputRoot { get; set; }
        public bool keepSubfolders { get; set; }
        public bool useGpu { get; set; }
        public string gpuDevices { get; set; }
        public int jpegQuality { get; set; }
        public int heatmapAlpha { get; set; }
        public int heatmapAlphaCut { get; set; }
        public bool heatmapImageSave { get; set; }
        public bool forceJet { get; set; }
        public int printEvery { get; set; }
        public List<AgentToolRequest> tools { get; set; }
        public List<AgentJudgementRequest> judgements { get; set; }
    }

    public sealed class AgentBlueOptions
    {
        public bool useGpu { get; set; }
        public string gpuDevices { get; set; }
        public bool keepSubfolders { get; set; }
        public bool saveAsJpeg { get; set; }
        public bool skipExisting { get; set; }
        public int jpegQuality { get; set; }
        public int printEvery { get; set; }
        public int cropWidth { get; set; }
        public int cropHeight { get; set; }
        public double expectedXMin { get; set; }
        public double expectedXMax { get; set; }
        public double maxYDiff { get; set; }
        public List<AgentBlueFallbackRequest> fallbacks { get; set; }
    }

    public sealed class AgentIntegratedOptions
    {
        public string cellIdCsvPath { get; set; }
        public bool keywordMode { get; set; }
        public string keywordInputRoot { get; set; }
        public bool keepCropImages { get; set; }
        public bool heatmapImageSave { get; set; }
    }

    public sealed class AgentToolRequest
    {
        public string toolName { get; set; }
        public double threshold { get; set; }
        public string judgement { get; set; }
    }

    public sealed class AgentJudgementRequest
    {
        public int priority { get; set; }
        public string name { get; set; }
    }

    public sealed class AgentBlueFallbackRequest
    {
        public string slotKey { get; set; }
        public string displayName { get; set; }
        public string toolName { get; set; }
        public int fallbackShiftX { get; set; }
        public int fallbackShiftY { get; set; }
        public int previewRoiX { get; set; }
        public int previewRoiY { get; set; }
        public int previewRoiW { get; set; }
        public int previewRoiH { get; set; }
        public string sampleImagePath { get; set; }
    }

    public sealed class AgentPositionRequest
    {
        public string key { get; set; }
        public string displayName { get; set; }
        public bool enabled { get; set; }

        // Shared workspace/stream settings between Integrated and standalone Green/Blue.
        public string greenWorkspacePath { get; set; }
        public string blueWorkspacePath { get; set; }
        public string greenImageRoot { get; set; }
        public string blueImageRoot { get; set; }
        public List<string> greenImageRoots { get; set; }
        public List<string> blueImageRoots { get; set; }
        public string greenStreamName { get; set; }
        public string blueStreamName { get; set; }
        public string blueToolName { get; set; }
        public string greenKeyword { get; set; }
        public string integratedKeyword { get; set; }

        // v0.1.x compatibility fields
        public string workspacePath { get; set; }
        public string imageRoot { get; set; }
        public string streamName { get; set; }
        public string keyword { get; set; }
    }

    public sealed class WorkspaceInspectionStream
    {
        public string name { get; set; }
        public List<WorkspaceInspectionTool> tools { get; set; }
    }

    public sealed class WorkspaceInspectionTool
    {
        public string name { get; set; }
        public string path { get; set; }
        public string type { get; set; }
        public List<string> tags { get; set; }
        public List<string> classes { get; set; }
        public List<string> features { get; set; }
    }

    public sealed class WorkspaceInspectionResponse
    {
        public bool ok { get; set; }
        public bool busy { get; set; }
        public string error { get; set; }
        public string path { get; set; }
        public string workspaceName { get; set; }
        public string loadMethod { get; set; }
        public int streamCount { get; set; }
        public int toolCount { get; set; }
        public List<WorkspaceInspectionStream> streams { get; set; }
        public List<string> warnings { get; set; }
    }

    public sealed class RuntimePreloadItem
    {
        public string positionKey { get; set; }
        public string displayName { get; set; }
        public string kind { get; set; }
        public WorkspaceInspectionResponse info { get; set; }
    }

    public sealed class RuntimePreloadResponse
    {
        public bool ok { get; set; }
        public string error { get; set; }
        public string token { get; set; }
        public string signature { get; set; }
        public string mode { get; set; }
        public string installedVpdlVersion { get; set; }
        public string vpdlVersion { get; set; }
        public int workspaceCount { get; set; }
        public long elapsedMs { get; set; }
        public List<RuntimePreloadItem> items { get; set; } = new List<RuntimePreloadItem>();
    }

    public sealed class SimulationState
    {
        public bool running { get; set; }
        public string mode { get; set; }
        public int processed { get; set; }
        public int total { get; set; }
        public int ok { get; set; }
        public int ng { get; set; }
        public string current { get; set; }
        public string message { get; set; }
        public string outputRoot { get; set; }
        public string resultCsv { get; set; }
        public string error { get; set; }
        public double elapsedSeconds { get; set; }
        public double etaSeconds { get; set; }
        public double imagesPerSecond { get; set; }
        public int batchSize { get; set; }
    }
}
