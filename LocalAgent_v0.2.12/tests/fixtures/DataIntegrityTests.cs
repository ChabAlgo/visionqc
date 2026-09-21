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
        Check(ResultCsv.SplitCaptureTimestamp("2026-08-07T01:20:30").SequenceEqual(new[]{"2026-08-07","01:20:30"}), "Capture Date/Time columns incorrect");
        Check(ResultCsv.SplitCaptureTimestamp(null).All(string.IsNullOrEmpty), "Unknown capture date invented");
        Check(ResultCsv.SplitCaptureTimestamp("2026-08-07").SequenceEqual(new[]{"2026-08-07",""}), "Known date lost or unknown time invented");
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
        // Partition boundaries count CSV records, not physical lines. Interleaved dates exercise LRU reopen.
        string split = Path.Combine(root, "split.csv");
        string primary;
        using (var writer = new PartitionedCsvWriter(split, 2, true, writeManifest:true))
        {
            writer.WriteLine("Cell ID,CaptureTimestamp,Note");
            for (int round = 0; round < 3; round++) for (int day = 1; day <= 12; day++)
                writer.WriteLine(ResultCsv.WriteRecord(new[] { "00" + day, "2026-09-" + day.ToString("D2") + "T12:00:00", "a\nb,\"c\"" }));
            writer.WriteLine("unknown,bad-date,x");
            writer.Flush(); primary = writer.PrimaryPath;
        }
        var parts = PartitionedCsvWriter.Expand(primary).ToArray();
        Check(parts.Length == 25, "Date/row partition count incorrect");
        int rowCount = 0;
        foreach (string part in parts) using (var reader = new StreamReader(part))
        {
            Check(ResultCsv.ReadHeader(reader).Count == 3, "Missing repeated header");
            int count = 0; System.Collections.Generic.List<string> row;
            while ((row = ResultCsv.ReadRecord(reader)) != null)
            {
                Check(row.Count == 3, "Quoted record damaged across parts");
                if (row[0] != "unknown") Check(row[2] == "a\nb,\"c\"", "Multiline data changed");
                count++;
            }
            Check(count <= 2, "Data row cap exceeded"); rowCount += count;
        }
        Check(rowCount == 37 && parts.Any(x => x.Contains("unknown-date")), "Partition lost rows or unknown date");
        // Parent merge consumes every child part before removing only its temporary inventory.
        File.WriteAllText(b,"Cell ID,CaptureTimestamp,Note\r\nlast,2026-09-01T12:00:00,tail\r\n");
        string mergeRoot=Path.Combine(root,"merged-parts");Directory.CreateDirectory(mergeRoot);
        ResultCsv.Merge(mergeRoot,new[]{primary,b},2,true);
        Check(!File.Exists(primary+".parts.txt") && parts.All(File.Exists),"Merge cleanup removed CSV or retained inventory");
        Check(Directory.GetFiles(mergeRoot,"*.parts.txt").Length==0,"Final merge left text inventory");
        int mergedRows=0;
        foreach(string file in Directory.GetFiles(mergeRoot,"*.csv"))using(var reader=new StreamReader(file)){ResultCsv.ReadHeader(reader);while(ResultCsv.ReadRecord(reader)!=null)mergedRows++;}
        Check(mergedRows==38,"Manifest cleanup lost a later/date-partitioned CSV");
        string retained;
        using(var writer=new PartitionedCsvWriter(Path.Combine(root,"retry.csv"),1,false,writeManifest:true)){writer.WriteLine("A,B");writer.WriteLine("1,2");writer.Flush();retained=writer.PrimaryPath;}
        File.WriteAllText(b,"A,B\r\nbad\r\n");
        Reject(()=>ResultCsv.Merge(mergeRoot,new[]{retained,b}));
        Check(File.Exists(retained+".parts.txt"),"Failed merge destroyed retry inventory");
        ResultCsv.Merge(mergeRoot,new[]{retained});
        Check(!File.Exists(retained+".parts.txt")&&File.Exists(retained),"Single Position cleanup mismatch");
        string empty;
        using (var writer = new PartitionedCsvWriter(Path.Combine(root, "empty.csv"), 2)) { writer.WriteLine("Cell ID,Position"); empty = writer.PrimaryPath; }
        Check(File.ReadAllLines(empty).Length == 1, "Empty run lost header");
        Check(!File.Exists(empty+".parts.txt"),"Normal CSV export created unnecessary text inventory");
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
