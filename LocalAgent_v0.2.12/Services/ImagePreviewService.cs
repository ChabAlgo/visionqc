using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;

namespace VisionQC.LocalAgent.Services
{
    // 브라우저가 로컬 디스크 경로를 직접 읽을 수 없으므로, 허용된 이미지 1장만 축소 JPEG로 반환한다.
    internal sealed class ImagePreviewService
    {
        private const long MaxSourceBytes = 512L * 1024L * 1024L;

        internal AgentImagePreviewResponse Create(AgentImagePreviewRequest request)
        {
            var response = new AgentImagePreviewResponse();
            try
            {
                string sourcePath = Path.GetFullPath((request == null ? "" : request.imagePath) ?? "");
                response.imagePath = sourcePath;
                if (!File.Exists(sourcePath)) return Fail(response, "이미지 파일을 찾을 수 없습니다.");
                if (!IsSupported(sourcePath)) return Fail(response, "지원하지 않는 이미지 확장자입니다.");

                var info = new FileInfo(sourcePath);
                if (info.Length > MaxSourceBytes) return Fail(response, "이미지 파일이 512 MB를 초과하여 미리보기를 만들 수 없습니다.");

                int maxDimension = request == null ? 2560 : request.maxDimension;
                maxDimension = Math.Max(512, Math.Min(4096, maxDimension <= 0 ? 2560 : maxDimension));
                using (var source = new Bitmap(sourcePath))
                {
                    if (source.Width < 1 || source.Height < 1) return Fail(response, "유효한 이미지 크기가 아닙니다.");
                    double scale = Math.Min(1d, maxDimension / (double)Math.Max(source.Width, source.Height));
                    int width = Math.Max(1, (int)Math.Round(source.Width * scale));
                    int height = Math.Max(1, (int)Math.Round(source.Height * scale));
                    using (var preview = new Bitmap(width, height, PixelFormat.Format24bppRgb))
                    using (Graphics graphics = Graphics.FromImage(preview))
                    using (var stream = new MemoryStream())
                    {
                        graphics.Clear(Color.White);
                        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        graphics.SmoothingMode = SmoothingMode.HighQuality;
                        graphics.DrawImage(source, new Rectangle(0, 0, width, height));
                        SaveJpeg(preview, stream, 90L);
                        response.ok = true;
                        response.width = width;
                        response.height = height;
                        response.resized = scale < 1d;
                        response.dataUrl = "data:image/jpeg;base64," + Convert.ToBase64String(stream.ToArray());
                        return response;
                    }
                }
            }
            catch (Exception ex)
            {
                return Fail(response, "이미지 미리보기 생성 실패: " + ex.Message);
            }
        }

        private static bool IsSupported(string path)
        {
            string extension = Path.GetExtension(path ?? "").ToLowerInvariant();
            return new[] { ".bmp", ".gif", ".jpg", ".jpeg", ".png", ".tif", ".tiff" }.Contains(extension);
        }

        private static AgentImagePreviewResponse Fail(AgentImagePreviewResponse response, string message)
        {
            response.ok = false;
            response.error = message;
            return response;
        }

        private static void SaveJpeg(Image image, Stream target, long quality)
        {
            ImageCodecInfo codec = ImageCodecInfo.GetImageEncoders().FirstOrDefault(x => x.MimeType == "image/jpeg");
            if (codec == null) { image.Save(target, ImageFormat.Jpeg); return; }
            using (var parameters = new EncoderParameters(1))
            {
                parameters.Param[0] = new EncoderParameter(Encoder.Quality, quality);
                image.Save(target, codec, parameters);
            }
        }
    }
}
