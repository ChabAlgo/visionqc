using System;
using System.Collections.Generic;
using VisionQC.LocalAgent.Domain;

namespace VisionQC.LocalAgent
{
    public class AgentStartRequest
    {
        public string mode { get; set; }
        public string outputRoot { get; set; }
        public AgentGreenOptions green { get; set; }
        public AgentBlueOptions blue { get; set; }
        public AgentIntegratedOptions integrated { get; set; }
        public List<AgentPositionRequest> positions { get; set; }
        public NamingProfile namingProfile { get; set; }

        // v0.1.x compatibility fields
        public bool useGpu { get; set; }
        public string gpuDevices { get; set; }
        public int jpegQuality { get; set; }
        public int printEvery { get; set; }
        public bool keepSubfolders { get; set; }
        public bool heatmapImageSave { get; set; }
        public bool keepCropImages { get; set; }
    }

    // 분류 화면의 VPDL Inspect는 시뮬레이션 설정을 재사용하되, 현재 이미지 한 장만 처리한다.
    public class AgentSingleInspectionRequest : AgentStartRequest
    {
        public string imagePath { get; set; }
    }

    // 분류 화면은 브라우저가 보유한 File 객체를 base64로 한 번만 전달한다.
    // Agent는 임시 파일로 검사한 뒤 즉시 제거하며 SQLite 검사 이력을 만들지 않는다.
    public sealed class AgentUploadedInspectionRequest : AgentSingleInspectionRequest
    {
        public string imageBase64 { get; set; }
        public string fileName { get; set; }
        public string mimeType { get; set; }
    }

    public sealed class AgentImagePreviewRequest
    {
        public string imagePath { get; set; }
        public int maxDimension { get; set; }
    }

    public sealed class AgentImagePreviewResponse
    {
        public bool ok { get; set; }
        public string error { get; set; }
        public string imagePath { get; set; }
        public string dataUrl { get; set; }
        public int width { get; set; }
        public int height { get; set; }
        public bool resized { get; set; }
    }

    // CSV는 브라우저가 보관하고, 사용자가 명시적으로 저장을 누를 때만 이 DTO로 SQLite 이력을 적재한다.
    public sealed class AgentHistoryImportRequest
    {
        public string importId { get; set; }
        public bool begin { get; set; }
        public bool complete { get; set; }
        public string sourceName { get; set; }
        public string mode { get; set; }
        public string webVersion { get; set; }
        public NamingProfile namingProfile { get; set; }
        public List<AgentHistoryRecordRequest> records { get; set; }
    }

    public sealed class AgentHistoryRecordRequest
    {
        public string sourceFileName { get; set; }
        public int sourceRowNumber { get; set; }
        public string fullPath { get; set; }
        public string processedPath { get; set; }
        public string cellId { get; set; }
        public string position { get; set; }
        public string totalResult { get; set; }
        public string judgement { get; set; }
        // CSV 열 또는 파일명 규칙에서 계산한 촬영 시각입니다. 없으면 파일명 규칙 결과를 사용합니다.
        public string captureTimestamp { get; set; }
        public List<AgentHistoryToolResultRequest> tools { get; set; }
    }

    public sealed class AgentHistoryToolResultRequest
    {
        public string tool { get; set; }
        public string result { get; set; }
        public double? score { get; set; }
        public string overlayPath { get; set; }
    }

    // SQLite 이력은 항상 Agent에서 페이지 단위로 조회합니다. 대량 이력을 브라우저 배열로 전송하지 않습니다.
    public sealed class AgentHistorySearchRequest
    {
        public string fromDate { get; set; }
        public string toDate { get; set; }
        public string cellId { get; set; }
        public string position { get; set; }
        public string tool { get; set; }
        public string toolResult { get; set; }
        public string totalResult { get; set; }
        public string sourceName { get; set; }
        public string fullPath { get; set; }
        public List<string> sourceTypes { get; set; }
        public int page { get; set; } = 1;
        public int pageSize { get; set; } = 50;
    }

    public sealed class AgentHistorySearchResponse
    {
        public bool ok { get; set; }
        public string error { get; set; }
        public string databasePath { get; set; }
        public int page { get; set; }
        public int pageSize { get; set; }
        public long totalCount { get; set; }
        public long ngCount { get; set; }
        public long uniqueCellCount { get; set; }
        public List<AgentHistoryDailySummary> daily { get; set; } = new List<AgentHistoryDailySummary>();
        public List<AgentHistoryImageRecord> items { get; set; } = new List<AgentHistoryImageRecord>();
    }

    public sealed class AgentHistoryDailySummary
    {
        public string date { get; set; }
        public long total { get; set; }
        public long ng { get; set; }
        public double ngRate { get; set; }
    }

    public sealed class AgentHistoryImageRecord
    {
        public long imageId { get; set; }
        public string runId { get; set; }
        public string sourceFileName { get; set; }
        public int sourceRowNumber { get; set; }
        public string fullPath { get; set; }
        public string processedPath { get; set; }
        public string cellId { get; set; }
        public string position { get; set; }
        public string totalResult { get; set; }
        public string judgement { get; set; }
        public string captureTimestamp { get; set; }
        public string inspectedAtUtc { get; set; }
        public List<AgentHistoryToolResultRequest> tools { get; set; } = new List<AgentHistoryToolResultRequest>();
    }

    public sealed class AgentHistoryFileImportRequest
    {
        public string filePath { get; set; }
        public string sourceName { get; set; }
        public string mode { get; set; }
        public string webVersion { get; set; }
        public string defaultPosition { get; set; }
        public NamingProfile namingProfile { get; set; }
    }

    public sealed class AgentHistoryFileImportStatus
    {
        public bool ok { get; set; }
        public bool running { get; set; }
        public bool completed { get; set; }
        public string jobId { get; set; }
        public string filePath { get; set; }
        public long processed { get; set; }
        public string error { get; set; }
        public string databasePath { get; set; }
    }

    public sealed class AgentGreenOptions
    {
        public string cellIdCsvPath { get; set; }
        public bool keywordMode { get; set; }
        public string keywordInputRoot { get; set; }
        public List<string> keywordInputRoots { get; set; }
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
        public List<string> keywordInputRoots { get; set; }
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
        public string controlSignature { get; set; }
        public string greenWorkspaceSignature { get; set; }
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
