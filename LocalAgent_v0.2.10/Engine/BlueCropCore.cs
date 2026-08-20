using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using SysInvalidOperationException = System.InvalidOperationException;
using ViDi2;
using Runtime = ViDi2.Runtime;
using LocalRuntime = ViDi2.Runtime.Local;
using LocalImages = ViDi2.Local;

namespace VpdlGreenHeatmapOverlay
{
    internal class BlueCropConfig
    {
        public List<BlueWorkspaceSlotConfig> Slots { get; set; } = new List<BlueWorkspaceSlotConfig>();
        public List<BlueToolFallbackConfig> ToolFallbacks { get; set; } = new List<BlueToolFallbackConfig>();
        public string OutputRoot { get; set; }
        public bool UseGpu { get; set; } = true;
        public List<int> GpuDevices { get; set; } = new List<int> { 0 };
        public int CropWidth { get; set; } = 2448;
        public int CropHeight { get; set; } = 2048;
        public double ExpectedXMin { get; set; } = 1100.0;
        public double ExpectedXMax { get; set; } = 1500.0;
        public double MaxYDiff { get; set; } = 300.0;
        public bool KeepSubfolders { get; set; } = true;
        public bool SaveAsJpeg { get; set; } = true;
        public int JpegQuality { get; set; } = 80;
        public bool SkipExisting { get; set; } = false;
        public int PrintEvery { get; set; } = 100;
    }

    internal class BlueWorkspaceSlotConfig
    {
        public string Key { get; set; }
        public string DisplayName { get; set; }
        public bool Enabled { get; set; }
        public string RuntimeWorkspacePath { get; set; }
        public string ImageRoot { get; set; }
        public string StreamName { get; set; }
        public string BlueToolName { get; set; }
        public string Keyword { get; set; }
    }

    internal class BlueToolFallbackConfig
    {
        public string SlotKey { get; set; }
        public string DisplayName { get; set; }
        public string ToolName { get; set; }
        public int FallbackShiftX { get; set; }
        public int FallbackShiftY { get; set; }
        public int PreviewRoiX { get; set; }
        public int PreviewRoiY { get; set; }
        public int PreviewRoiW { get; set; }
        public int PreviewRoiH { get; set; }
        public string SampleImagePath { get; set; }
    }

    internal class BlueProcessSummary
    {
        public int TotalImages { get; set; }
        public int ProcessedImages { get; set; }
        public int SavedImages { get; set; }
        public int SkippedExisting { get; set; }
        public int SkippedByCellIdCount { get; set; }
        public int FallbackCount { get; set; }
        public int ErrorCount { get; set; }
        public string OutputRoot { get; set; }
        public TimeSpan Elapsed { get; set; }
        public List<string> FallbackImages { get; set; } = new List<string>();
        public List<string> ErrorImages { get; set; } = new List<string>();
        public Dictionary<string, int> PositionImageCount { get; set; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> PositionSavedCount { get; set; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    }

    internal class BlueImageProcessResult
    {
        public bool Saved { get; set; }
        public bool SkippedExisting { get; set; }
        public bool UsedFallback { get; set; }
        public bool Error { get; set; }
        public string Message { get; set; }
        public string OutputPath { get; set; }
    }

    internal static class BlueCropProcessor
    {
        private static readonly HashSet<string> ImageExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".bmp", ".jpg", ".jpeg", ".tif", ".tiff"
        };

        public static BlueProcessSummary Run(BlueCropConfig config, IProgress<ProcessProgress> progress, CancellationToken token)
        {
            return Run(config, null, false, progress, token);
        }

        internal static BlueProcessSummary Run(BlueCropConfig config, LocalRuntime.Control sharedControl, bool reusePreloadedWorkspaces, IProgress<ProcessProgress> progress, CancellationToken token)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (string.IsNullOrWhiteSpace(config.OutputRoot)) throw new SysInvalidOperationException("Blue 출력 폴더를 선택하세요.");
            Directory.CreateDirectory(config.OutputRoot);

            var sw = Stopwatch.StartNew();
            var summary = new BlueProcessSummary { OutputRoot = config.OutputRoot };
            var enabledSlots = config.Slots.Where(s => s.Enabled).ToList();

            var slotImages = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            int total = 0;
            foreach (var slot in enabledSlots)
            {
                var images = EnumerateImages(slot.ImageRoot, config.OutputRoot)
                    .Where(p => FileNameMatchesKeyword(Path.GetFileName(p), slot.Keyword))
                    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                slotImages[slot.Key] = images;
                summary.PositionImageCount[slot.DisplayName] = images.Count;
                total += images.Count;
                Directory.CreateDirectory(GetPositionOutputRoot(config, slot));
            }
            summary.TotalImages = total;
            progress?.Report(new ProcessProgress { Message = "[Blue] 대상 이미지: " + total, Processed = 0, Total = total });

            var gpuMode = config.UseGpu ? GpuMode.SingleDevicePerTool : GpuMode.NoSupport;
            var gpuList = config.UseGpu ? config.GpuDevices : new List<int>();
            int processed = 0;

            var control = sharedControl ?? new LocalRuntime.Control(gpuMode, gpuList);
            bool ownsControl = sharedControl == null;
            try
            {
                foreach (var slot in enabledSlots)
                {
                    token.ThrowIfCancellationRequested();
                    var images = slotImages[slot.Key];
                    progress?.Report(new ProcessProgress { Message = "[Blue] " + slot.DisplayName + (reusePreloadedWorkspaces ? " 사전 로드 Runtime 연결 중..." : " 워크스페이스 로드 중...") });
                    Runtime.IWorkspace workspace = ResolveWorkspace(control, "blue_" + slot.Key, slot.RuntimeWorkspacePath, reusePreloadedWorkspaces);
                    Runtime.IStream stream = GetStream(workspace, slot.StreamName);
                    Runtime.ITool blueTool = GetTool(stream, slot.BlueToolName);
                    progress?.Report(new ProcessProgress { Message = string.Format("[Blue] {0} Stream/Tool 확인 완료: {1} / {2} | 이미지 {3}개", slot.DisplayName, stream.Name, blueTool.Name, images.Count) });

                    foreach (string imagePath in images)
                    {
                        token.ThrowIfCancellationRequested();
                        processed++;
                        summary.ProcessedImages = processed;
                        var result = ProcessOneImage(config, slot, stream, blueTool, imagePath);
                        if (result.Saved)
                        {
                            summary.SavedImages++;
                            if (!summary.PositionSavedCount.ContainsKey(slot.DisplayName)) summary.PositionSavedCount[slot.DisplayName] = 0;
                            summary.PositionSavedCount[slot.DisplayName]++;
                        }
                        if (result.SkippedExisting) summary.SkippedExisting++;
                        if (result.UsedFallback)
                        {
                            summary.FallbackCount++;
                            summary.FallbackImages.Add(slot.DisplayName + " | " + imagePath + " | " + result.Message);
                        }
                        if (result.Error)
                        {
                            summary.ErrorCount++;
                            summary.ErrorImages.Add(slot.DisplayName + " | " + imagePath + " | " + result.Message);
                        }
                        progress?.Report(new ProcessProgress
                        {
                            Message = string.Format("[Blue] 진행 {0}/{1} | 저장 {2} | Fallback {3} | Error {4}", processed, total, summary.SavedImages, summary.FallbackCount, summary.ErrorCount),
                            Processed = processed, Total = total, CurrentFile = imagePath
                        });
                        if (processed == 1 || processed == total || processed % Math.Max(1, config.PrintEvery) == 0)
                        {
                            string msg = string.Format("[Blue] 진행 {0}/{1} | 저장 {2} | Fallback {3} | Error {4}", processed, total, summary.SavedImages, summary.FallbackCount, summary.ErrorCount);
                            progress?.Report(new ProcessProgress { Message = msg, Processed = processed, Total = total });
                        }
                    }
                }
            }
            finally
            {
                if (ownsControl) control.Dispose();
            }
            sw.Stop();
            summary.Elapsed = sw.Elapsed;
            return summary;
        }

        internal static Runtime.IWorkspace ResolveWorkspace(LocalRuntime.Control control, string workspaceName, string workspacePath, bool reusePreloaded)
        {
            if (!reusePreloaded) return control.Workspaces.Add(workspaceName, workspacePath);
            Runtime.IWorkspace workspace;
            if (RuntimeWorkspaceRegistry.TryGet(control, workspaceName, out workspace)) return workspace;
            throw new SysInvalidOperationException("사전 로드 Runtime을 찾지 못했습니다: " + workspaceName + ". Runtime File Load를 다시 실행하세요.");
        }

        internal static Runtime.IStream GetStream(Runtime.IWorkspace workspace, string streamName)
        {
            foreach (Runtime.IStream s in workspace.Streams)
                if (string.Equals(s.Name, streamName, StringComparison.OrdinalIgnoreCase)) return s;
            string names = string.Join(", ", workspace.Streams.Cast<Runtime.IStream>().Select(s => s.Name));
            throw new SysInvalidOperationException("Stream을 찾지 못했습니다: " + streamName + Environment.NewLine + "현재 Streams: " + names);
        }

        internal static Runtime.ITool GetTool(Runtime.IStream stream, string toolName)
        {
            foreach (Runtime.ITool t in stream.Tools)
                if (string.Equals(t.Name, toolName, StringComparison.OrdinalIgnoreCase)) return t;
            string names = string.Join(", ", stream.Tools.Cast<Runtime.ITool>().Select(t => t.Name));
            throw new SysInvalidOperationException("Blue Tool을 찾지 못했습니다: " + toolName + Environment.NewLine + "현재 Tools: " + names);
        }

        internal static bool FileNameMatchesKeyword(string fileName, string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword)) return true;
            return (fileName ?? string.Empty).IndexOf(keyword.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal static IEnumerable<string> EnumerateImages(string imageRoot, string outputRoot)
        {
            string normalizedOutput = NormalizeDir(outputRoot);
            foreach (string path in Directory.EnumerateFiles(imageRoot, "*.*", SearchOption.AllDirectories))
            {
                string ext = Path.GetExtension(path);
                if (!ImageExtensions.Contains(ext)) continue;
                if (!string.IsNullOrWhiteSpace(normalizedOutput) && IsUnderDirectory(path, normalizedOutput)) continue;
                if (path.IndexOf(Path.DirectorySeparatorChar + "_BlueCrop" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) >= 0) continue;
                yield return path;
            }
        }

        internal static BlueImageProcessResult ProcessOneImage(BlueCropConfig config, BlueWorkspaceSlotConfig slot, Runtime.IStream stream, Runtime.ITool blueTool, string imagePath)
        {
            try
            {
                string outputPath = BuildOutputPath(config, slot, imagePath);
                if (config.SkipExisting && File.Exists(outputPath)) return new BlueImageProcessResult { SkippedExisting = true, OutputPath = outputPath };
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath));

                using (var vidiImage = new LocalImages.LibraryImage(imagePath))
                using (ISample sample = stream.CreateSample())
                {
                    sample.AddImage(vidiImage);
                    sample.Process(blueTool);
                    double centerX = 0, centerY = 0;
                    bool useFallback = false;
                    string fallbackReason = "";
                    var marking = sample.Markings[blueTool.Name] as IBlueMarking;
                    if (marking == null || marking.Views == null || marking.Views.Count == 0)
                    {
                        useFallback = true;
                        fallbackReason = "IBlueMarking/View 없음";
                    }
                    else
                    {
                        IBlueView view = marking.Views[0];
                        ViDi2.IFeature p1Feat = null;
                        ViDi2.IFeature p2Feat = null;
                        foreach (ViDi2.IFeature f in view.Features)
                        {
                            string name = f.Name ?? string.Empty;
                            if (p1Feat == null && name.Equals("p1", StringComparison.OrdinalIgnoreCase)) p1Feat = f;
                            else if (p2Feat == null && name.Equals("p2", StringComparison.OrdinalIgnoreCase)) p2Feat = f;
                        }
                        if (p1Feat == null || p2Feat == null)
                        {
                            useFallback = true;
                            fallbackReason = "Feature p1/p2 없음";
                        }
                        else
                        {
                            var p1 = p1Feat.Position;
                            var p2 = p2Feat.Position;
                            double xDist = Math.Abs(p2.X - p1.X);
                            double yDist = Math.Abs(p2.Y - p1.Y);
                            if (xDist < config.ExpectedXMin || xDist > config.ExpectedXMax || yDist > config.MaxYDiff)
                            {
                                useFallback = true;
                                fallbackReason = string.Format("거리 조건 불만족 xDist={0:F1}, yDist={1:F1}", xDist, yDist);
                            }
                            else
                            {
                                centerX = (p1.X + p2.X) / 2.0;
                                centerY = (p1.Y + p2.Y) / 2.0;
                                CropAroundCenter(config, vidiImage, centerX, centerY, outputPath);
                                return new BlueImageProcessResult { Saved = true, OutputPath = outputPath };
                            }
                        }
                    }

                    BlueToolFallbackConfig fb = GetFallbackConfig(config, slot, blueTool.Name);
                    centerX = vidiImage.Width / 2.0 + fb.FallbackShiftX;
                    centerY = vidiImage.Height / 2.0 + fb.FallbackShiftY;
                    CropAroundCenter(config, vidiImage, centerX, centerY, outputPath);
                    return new BlueImageProcessResult { Saved = true, UsedFallback = useFallback, Message = fallbackReason, OutputPath = outputPath };
                }
            }
            catch (System.Exception ex)
            {
                return new BlueImageProcessResult { Error = true, Message = ex.Message };
            }
        }

        private static BlueToolFallbackConfig GetFallbackConfig(BlueCropConfig config, BlueWorkspaceSlotConfig slot, string toolName)
        {
            if (config.ToolFallbacks != null)
            {
                foreach (var fb in config.ToolFallbacks)
                {
                    if (string.Equals(fb.SlotKey, slot.Key, StringComparison.OrdinalIgnoreCase) && string.Equals(fb.ToolName, toolName, StringComparison.OrdinalIgnoreCase)) return fb;
                }
            }
            return new BlueToolFallbackConfig { SlotKey = slot.Key, DisplayName = slot.DisplayName, ToolName = toolName, FallbackShiftX = 0, FallbackShiftY = 200 };
        }

        private static void CropAroundCenter(BlueCropConfig config, LocalImages.LibraryImage vidiImage, double centerX, double centerY, string outputPath)
        {
            int imgW = vidiImage.Width;
            int imgH = vidiImage.Height;
            int cropW = Math.Min(config.CropWidth, imgW);
            int cropH = Math.Min(config.CropHeight, imgH);
            int cx = (int)Math.Round(centerX);
            int cy = (int)Math.Round(centerY);
            int x = cx - cropW / 2;
            int y = cy - cropH / 2;
            if (x < 0) x = 0;
            if (y < 0) y = 0;
            if (x + cropW > imgW) x = imgW - cropW;
            if (y + cropH > imgH) y = imgH - cropH;
            if (x < 0) x = 0;
            if (y < 0) y = 0;
            if (cropW <= 0 || cropH <= 0) throw new SysInvalidOperationException("크롭 영역이 유효하지 않습니다.");
            using (Bitmap src = vidiImage.Bitmap)
            using (Bitmap cropped = src.Clone(new Rectangle(x, y, cropW, cropH), src.PixelFormat))
            {
                ImageSaveHelper.Save(cropped, outputPath, config.SaveAsJpeg, config.JpegQuality);
            }
        }

        private static string BuildOutputPath(BlueCropConfig config, BlueWorkspaceSlotConfig slot, string sourceFullPath)
        {
            string rel = GetRelativePath(slot.ImageRoot, sourceFullPath);
            string dir = config.KeepSubfolders ? (Path.GetDirectoryName(rel) ?? "") : "";
            string nameWithout = Path.GetFileNameWithoutExtension(rel);
            string ext = config.SaveAsJpeg ? ".jpg" : Path.GetExtension(rel);
            if (string.IsNullOrWhiteSpace(ext)) ext = ".jpg";
            string outDir = Path.Combine(GetPositionOutputRoot(config, slot), dir);
            return Path.Combine(outDir, nameWithout + "_Blue" + ext);
        }

        private static string GetPositionOutputRoot(BlueCropConfig config, BlueWorkspaceSlotConfig slot)
        {
            return Path.Combine(config.OutputRoot, slot.DisplayName);
        }

        private static string GetRelativePath(string basePath, string fullPath)
        {
            if (!basePath.EndsWith(Path.DirectorySeparatorChar.ToString())) basePath += Path.DirectorySeparatorChar;
            Uri baseUri = new Uri(basePath);
            Uri fullUri = new Uri(fullPath);
            Uri relUri = baseUri.MakeRelativeUri(fullUri);
            string relPath = Uri.UnescapeDataString(relUri.ToString());
            return relPath.Replace('/', Path.DirectorySeparatorChar);
        }

        private static string NormalizeDir(string dir)
        {
            if (string.IsNullOrWhiteSpace(dir)) return "";
            try { return Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar; }
            catch { return ""; }
        }

        private static bool IsUnderDirectory(string filePath, string normalizedDir)
        {
            try { return Path.GetFullPath(filePath).StartsWith(normalizedDir, StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }
    }

    internal static class ImageSaveHelper
    {
        public static void Save(Bitmap bmp, string path, bool saveAsJpeg, int quality)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            if (!saveAsJpeg)
            {
                bmp.Save(path);
                return;
            }
            ImageCodecInfo jpgEncoder = ImageCodecInfo.GetImageEncoders().FirstOrDefault(c => c.FormatID == System.Drawing.Imaging.ImageFormat.Jpeg.Guid);
            if (jpgEncoder == null)
            {
                bmp.Save(path, System.Drawing.Imaging.ImageFormat.Jpeg);
                return;
            }
            using (var encParams = new EncoderParameters(1))
            {
                encParams.Param[0] = new EncoderParameter(Encoder.Quality, Math.Max(1, Math.Min(100, quality)));
                bmp.Save(path, jpgEncoder, encParams);
            }
        }
    }

    internal static class BluePreviewHelper
    {
        public static Bitmap CropFallback(Bitmap src, int cropW, int cropH, int shiftX, int shiftY)
        {
            cropW = Math.Min(cropW, src.Width);
            cropH = Math.Min(cropH, src.Height);
            int cx = src.Width / 2 + shiftX;
            int cy = src.Height / 2 + shiftY;
            var r = ClampRect(new Rectangle(cx - cropW / 2, cy - cropH / 2, cropW, cropH), src.Width, src.Height);
            return src.Clone(r, src.PixelFormat);
        }

        public static Bitmap CropRoi(Bitmap src, Rectangle roi)
        {
            var r = ClampRect(roi, src.Width, src.Height);
            return src.Clone(r, src.PixelFormat);
        }

        public static Bitmap DrawFallbackRect(Bitmap src, int cropW, int cropH, int shiftX, int shiftY)
        {
            var bmp = (Bitmap)src.Clone();
            cropW = Math.Min(cropW, src.Width);
            cropH = Math.Min(cropH, src.Height);
            int cx = src.Width / 2 + shiftX;
            int cy = src.Height / 2 + shiftY;
            var r = ClampRect(new Rectangle(cx - cropW / 2, cy - cropH / 2, cropW, cropH), src.Width, src.Height);
            using (var g = Graphics.FromImage(bmp)) using (var pen = new Pen(Color.Red, Math.Max(2, src.Width / 700))) g.DrawRectangle(pen, r);
            return bmp;
        }

        public static Bitmap DrawRoiRect(Bitmap src, Rectangle roi)
        {
            var bmp = (Bitmap)src.Clone();
            var r = ClampRect(roi, src.Width, src.Height);
            using (var g = Graphics.FromImage(bmp)) using (var pen = new Pen(Color.Lime, Math.Max(2, src.Width / 700))) g.DrawRectangle(pen, r);
            return bmp;
        }

        private static Rectangle ClampRect(Rectangle roi, int w, int h)
        {
            int x = Math.Max(0, roi.X), y = Math.Max(0, roi.Y);
            int rw = Math.Max(1, roi.Width), rh = Math.Max(1, roi.Height);
            if (x + rw > w) rw = w - x;
            if (y + rh > h) rh = h - y;
            if (rw <= 0) rw = 1;
            if (rh <= 0) rh = 1;
            return new Rectangle(x, y, rw, rh);
        }
    }

    internal class IntegratedProcessSummary
    {
        public BlueProcessSummary BlueSummary { get; set; }
        public ProcessSummary GreenSummary { get; set; }
        public TimeSpan Elapsed { get; set; }
    }

    internal static class IntegratedSimulationProcessor
    {
        public static IntegratedProcessSummary Run(BlueCropConfig blueConfig, AppConfig greenConfig, bool keepCropImages, string cropRoot, IProgress<ProcessProgress> progress, CancellationToken token)
        {
            // Backward-compatible batch mode. Kept for reference.
            var sw = Stopwatch.StartNew();
            progress?.Report(new ProcessProgress { Message = "[Integrated] Step 1/2: Blue Crop 시작" });
            var blueSummary = BlueCropProcessor.Run(blueConfig, progress, token);
            progress?.Report(new ProcessProgress { Message = "[Integrated] Step 2/2: Green 검사 시작" });
            var greenSummary = GreenOverlayProcessor.Run(greenConfig, progress, token);
            if (!keepCropImages)
            {
                try { if (Directory.Exists(cropRoot)) Directory.Delete(cropRoot, true); }
                catch { }
            }
            sw.Stop();
            return new IntegratedProcessSummary { BlueSummary = blueSummary, GreenSummary = greenSummary, Elapsed = sw.Elapsed };
        }

        public static IntegratedProcessSummary RunStreaming(BlueCropConfig blueConfig, AppConfig greenConfig, bool keepCropImages, string cropRoot, IProgress<ProcessProgress> progress, CancellationToken token)
        {
            return RunStreaming(blueConfig, greenConfig, keepCropImages, cropRoot, null, false, progress, token);
        }

        internal static IntegratedProcessSummary RunStreaming(BlueCropConfig blueConfig, AppConfig greenConfig, bool keepCropImages, string cropRoot, LocalRuntime.Control sharedControl, bool reusePreloadedWorkspaces, IProgress<ProcessProgress> progress, CancellationToken token)
        {
            if (blueConfig == null) throw new ArgumentNullException(nameof(blueConfig));
            if (greenConfig == null) throw new ArgumentNullException(nameof(greenConfig));
            if (string.IsNullOrWhiteSpace(cropRoot)) throw new SysInvalidOperationException("Crop Root가 지정되지 않았습니다.");

            var sw = Stopwatch.StartNew();
            var blueSummary = new BlueProcessSummary { OutputRoot = cropRoot };
            Directory.CreateDirectory(cropRoot);

            HashSet<string> integratedCellFilter = GreenOverlayProcessor.LoadCellIdFilterForExternalUse(greenConfig.CellIdCsvPath);
            bool useCellFilter = !string.IsNullOrWhiteSpace(greenConfig.CellIdCsvPath);
            if (useCellFilter && integratedCellFilter.Count == 0)
                throw new SysInvalidOperationException("Integrated Cell ID CSV를 선택했지만 읽을 수 있는 J/P 16자리 Cell ID가 0개입니다.");
            if (useCellFilter)
                progress?.Report(new ProcessProgress { Message = string.Format("[Integrated] Cell ID 필터 로드 완료: {0} IDs", integratedCellFilter.Count) });

            var enabledSlots = blueConfig.Slots.Where(s => s.Enabled).ToList();
            var slotImages = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            int total = 0;
            int originalTotal = 0;
            int skippedByCellId = 0;
            foreach (var slot in enabledSlots)
            {
                var allImages = BlueCropProcessor.EnumerateImages(slot.ImageRoot, cropRoot)
                    .Where(p => BlueCropProcessor.FileNameMatchesKeyword(Path.GetFileName(p), slot.Keyword))
                    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                originalTotal += allImages.Count;
                var images = allImages;
                if (useCellFilter)
                {
                    images = allImages
                        .Where(p => GreenOverlayProcessor.FileNameMatchesCellIdFilter(Path.GetFileName(p), integratedCellFilter))
                        .ToList();
                    skippedByCellId += allImages.Count - images.Count;
                }
                slotImages[slot.Key] = images;
                blueSummary.PositionImageCount[slot.DisplayName] = images.Count;
                total += images.Count;
                Directory.CreateDirectory(Path.Combine(cropRoot, slot.DisplayName));
            }
            blueSummary.TotalImages = total;
            blueSummary.SkippedByCellIdCount = skippedByCellId;
            if (useCellFilter)
                progress?.Report(new ProcessProgress { Message = string.Format("[Integrated] 원본 이미지 {0}개 → Cell ID 필터 적용 후 대상 {1}개, Skip {2}개", originalTotal, total, skippedByCellId), Processed = 0, Total = Math.Max(1, total) });
            progress?.Report(new ProcessProgress { Message = string.Format("[Integrated] Streaming 방식 시작: Blue Crop 1장 → Green 검사 1장 | 대상 {0}개", total), Processed = 0, Total = Math.Max(1, total) });

            // Integrated 모드에서는 Cell ID 필터를 Blue Crop 전에 이미 적용합니다.
            // Crop 파일명 변형(_Blue 등) 때문에 Green 단계에서 다시 필터링하면 오검출 Skip 위험이 있어 Green 내부 필터는 비웁니다.
            int integratedFilterCount = integratedCellFilter.Count;
            greenConfig.CellIdCsvPath = "";

            var gpuMode = blueConfig.UseGpu ? GpuMode.SingleDevicePerTool : GpuMode.NoSupport;
            var gpuList = blueConfig.UseGpu ? blueConfig.GpuDevices : new List<int>();
            int processed = 0;

            var blueControl = sharedControl ?? new LocalRuntime.Control(gpuMode, gpuList);
            bool ownsControl = sharedControl == null;
            try
            {
              using (var greenSession = GreenOverlayProcessor.BeginStreaming(greenConfig, blueControl, reusePreloadedWorkspaces, progress, token))
              {
                progress?.Report(new ProcessProgress { Message = "[Integrated] VPDL Control 1개를 Blue/Green이 공유하여 사용합니다." });
                foreach (var slot in enabledSlots)
                {
                    token.ThrowIfCancellationRequested();
                    var images = slotImages[slot.Key];
                    progress?.Report(new ProcessProgress { Message = "[Blue] " + slot.DisplayName + (reusePreloadedWorkspaces ? " 사전 로드 Runtime 연결 중..." : " 워크스페이스 로드 중...") });
                    Runtime.IWorkspace workspace = BlueCropProcessor.ResolveWorkspace(blueControl, "blue_" + slot.Key, slot.RuntimeWorkspacePath, reusePreloadedWorkspaces);
                    Runtime.IStream stream = BlueCropProcessor.GetStream(workspace, slot.StreamName);
                    Runtime.ITool blueTool = BlueCropProcessor.GetTool(stream, slot.BlueToolName);
                    progress?.Report(new ProcessProgress { Message = string.Format("[Blue] {0} Stream/Tool 확인 완료: {1} / {2} | 이미지 {3}개", slot.DisplayName, stream.Name, blueTool.Name, images.Count) });

                    string greenInputRoot = Path.Combine(cropRoot, slot.DisplayName);
                    foreach (string imagePath in images)
                    {
                        token.ThrowIfCancellationRequested();
                        processed++;
                        blueSummary.ProcessedImages = processed;

                        var result = BlueCropProcessor.ProcessOneImage(blueConfig, slot, stream, blueTool, imagePath);
                        if (result.Saved)
                        {
                            blueSummary.SavedImages++;
                            if (!blueSummary.PositionSavedCount.ContainsKey(slot.DisplayName)) blueSummary.PositionSavedCount[slot.DisplayName] = 0;
                            blueSummary.PositionSavedCount[slot.DisplayName]++;
                        }
                        if (result.SkippedExisting) blueSummary.SkippedExisting++;
                        if (result.UsedFallback)
                        {
                            blueSummary.FallbackCount++;
                            blueSummary.FallbackImages.Add(slot.DisplayName + " | " + imagePath + " | " + result.Message);
                        }
                        if (result.Error)
                        {
                            blueSummary.ErrorCount++;
                            blueSummary.ErrorImages.Add(slot.DisplayName + " | " + imagePath + " | " + result.Message);
                        }

                        if (!result.Error && !string.IsNullOrWhiteSpace(result.OutputPath) && File.Exists(result.OutputPath))
                        {
                            greenSession.ProcessImage(slot.Key, result.OutputPath, greenInputRoot, token);
                            if (!keepCropImages)
                            {
                                try { File.Delete(result.OutputPath); }
                                catch { }
                            }
                        }

                        progress?.Report(new ProcessProgress
                        {
                            Message = string.Format("[Integrated] 진행 {0}/{1} | Green OK={2}, NG={3}", processed, total, greenSession.TotalOkCount, greenSession.TotalNgCount),
                            Processed = processed, Total = total, OkCount = greenSession.TotalOkCount, NgCount = greenSession.TotalNgCount, CurrentFile = imagePath
                        });

                        if (processed == 1 || processed == total || processed % Math.Max(1, blueConfig.PrintEvery) == 0)
                        {
                            string msg = string.Format("[Integrated] 진행 {0}/{1} | Blue 저장 {2}, Fallback {3}, Error {4} | Green OK {5}, NG {6}",
                                processed, total, blueSummary.SavedImages, blueSummary.FallbackCount, blueSummary.ErrorCount, greenSession.TotalOkCount, greenSession.TotalNgCount);
                            progress?.Report(new ProcessProgress { Message = msg, Processed = processed, Total = total });
                        }
                    }
                }

                var greenSummary = greenSession.Finish();
                greenSummary.FilterCellIdCount = integratedFilterCount;
                greenSummary.SkippedByCellIdCount = skippedByCellId;
                if (!keepCropImages)
                {
                    try { if (Directory.Exists(cropRoot)) Directory.Delete(cropRoot, true); }
                    catch { }
                }
                sw.Stop();
                blueSummary.Elapsed = sw.Elapsed;
                return new IntegratedProcessSummary { BlueSummary = blueSummary, GreenSummary = greenSummary, Elapsed = sw.Elapsed };
              }
            }
            finally
            {
                if (ownsControl) blueControl.Dispose();
            }
        }
    }
}
