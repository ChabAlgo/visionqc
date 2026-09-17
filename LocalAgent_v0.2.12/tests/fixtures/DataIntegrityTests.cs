using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using VisionQC.LocalAgent.Services;

class DataIntegrityTests
{
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static void Reject(Action action) { try { action(); } catch (InvalidDataException) { return; } throw new Exception("Invalid CSV accepted"); }
    static void Main()
    {
        string root = Path.Combine(Path.GetTempPath(), "VisionQC-integrity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string a = Path.Combine(root, "a.csv"), b = Path.Combine(root, "b.csv");
        string workspace = Path.Combine(root,"workspace.vrws");
        File.WriteAllText(workspace,"original");
        var timestamp = File.GetLastWriteTimeUtc(workspace);
        string loaded = WorkspaceIdentity.Fingerprint(workspace);
        Check(loaded == WorkspaceIdentity.Fingerprint(workspace), "Unchanged workspace cannot reuse");
        File.WriteAllText(workspace,"modified"); File.SetLastWriteTimeUtc(workspace,timestamp);
        Check(loaded != WorkspaceIdentity.Fingerprint(workspace), "Same-size/same-time overwrite reused stale runtime");
        Check(loaded != WorkspaceIdentity.Fingerprint(workspace+"missing"), "Missing workspace reused stale runtime");
        File.WriteAllText(a, "Cell ID,Position,Crack_result,Crack_score,Note\r\n001,CA(TOP),NG,0.8,\"line1\r\nline2,\"\"quoted\"\"\"\r\n");
        File.WriteAllText(b, "Cell ID,Position,FoilDamage_score,Crack_score,FoilDamage_result,Crack_result\r\n002,AN(TOP),0.9,0.7,OK,NG\r\n");
        string merged = ResultCsv.Merge(root, new[] { a, b });
        using (var reader = new StreamReader(merged))
        {
            var header = ResultCsv.ReadHeader(reader); var first = ResultCsv.ReadRecord(reader); var second = ResultCsv.ReadRecord(reader);
            Check(first[0] == "001" && first[header.IndexOf("Note")] == "line1\r\nline2,\"quoted\"", "Quoted multiline or leading zero lost");
            Check(second[header.IndexOf("Crack_score")] == "0.7" && second[header.IndexOf("FoilDamage_score")] == "0.9", "Tool mapping shifted");
            Check(first[header.IndexOf("FoilDamage_score")] == "" && ResultCsv.ReadRecord(reader) == null, "Row loss or invented tool score");
        }
        Reject(() => ResultCsv.ReadRecord(new StringReader("\"unfinished")));
        Reject(() => ResultCsv.ReadHeader(new StringReader("A,a\n")));
        File.WriteAllText(b, "A,B\r\nonly-one\r\n");
        int outputs = Directory.GetFiles(root, "results_*.csv").Length;
        Reject(() => ResultCsv.Merge(root, new[] { a, b }));
        Check(Directory.GetFiles(root, "results_*.csv").Length == outputs && Directory.GetFiles(root, "*.tmp").Length == 0, "Partial output published");
        CultureInfo.CurrentCulture = new CultureInfo("de-DE");
        Check(ResultCsv.ParseScore("0,75") == .75 && ResultCsv.ParseScore("0.75") == .75 && ResultCsv.ParseScore("") == null, "Culture-dependent score");
        Reject(() => ResultCsv.ParseScore("75")); Reject(() => ResultCsv.ParseScore("NaN"));
        var corners = OverlayGeometry.Corners(new double[] { 0,1,-1,0,20.25,10.5,4,2 });
        Check(corners[0] == new PointF(20.25f,10.5f) && corners[1] == new PointF(20.25f,14.5f) && corners[2] == new PointF(18.25f,10.5f), "Rotated fractional ROI incorrect");
        Check(OverlayGeometry.Corners(new double[] { 0,0,0,0,0,0,4,2 }) == null, "Singular pose accepted");
        // A half-outside ROI must clip half of the original heatmap, never shrink the full map into it.
        using (var heat = new Bitmap(4,2)) using (var output = new Bitmap(4,4)) using (var g = Graphics.FromImage(output))
        {
            using (var h = Graphics.FromImage(heat)) { h.Clear(Color.Blue); h.FillRectangle(Brushes.Red,0,0,2,2); }
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            g.DrawImage(heat, OverlayGeometry.Corners(new double[] {1,0,0,1,-2,1,4,2}), new RectangleF(0,0,4,2), GraphicsUnit.Pixel);
            Check(output.GetPixel(1,1).B > 200 && output.GetPixel(1,1).R < 20, "Clipped heatmap was stretched");
        }
        Console.WriteLine("PASS: CSV header union, multiline, precision/culture, invalid rows, atomic output, ROI rotation/fraction/clipping. " + root);
    }
}
