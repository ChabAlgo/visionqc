using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace VisionQC.LocalAgent.Services
{
    // Each call is one complete CSV record, even when a quoted field contains a newline.
    // At most eight buffered file streams are open; no image/result rows are retained.
    internal sealed class PartitionedCsvWriter : TextWriter
    {
        private sealed class Bucket { internal int Part; internal long Count; internal string Path; }
        private sealed class OpenFile { internal StreamWriter Writer; internal long Access; }
        private readonly string _basePath;
        private readonly long _maxRows;
        private readonly bool _byDate;
        private readonly Dictionary<string, Bucket> _buckets = new Dictionary<string, Bucket>();
        private readonly Dictionary<string, OpenFile> _open = new Dictionary<string, OpenFile>();
        private readonly List<string> _paths = new List<string>();
        private readonly Dictionary<string, long> _counts = new Dictionary<string, long>();
        private string _header;
        private int _columns, _timestampIndex = -1, _dateIndex = -1;
        private long _clock;
        private bool _disposed;
        public override Encoding Encoding { get { return new UTF8Encoding(true); } }
        internal string PrimaryPath { get { EnsureEmptyOutput(); return _paths[0]; } }
        internal string[] Paths { get { return _paths.ToArray(); } }

        internal PartitionedCsvWriter(string path, long maxRows = 1000000, bool byDate = false)
        {
            if (maxRows <= 0) throw new ArgumentOutOfRangeException(nameof(maxRows));
            _basePath = Path.GetFullPath(path); _maxRows = maxRows; _byDate = byDate;
            Directory.CreateDirectory(Path.GetDirectoryName(_basePath));
        }
        public override void WriteLine(string value)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(PartitionedCsvWriter));
            var fields = ResultCsv.ReadRecord(new StringReader(value ?? ""));
            if (_header == null)
            {
                if (fields == null || fields.Count == 0) throw new InvalidDataException("CSV header is empty");
                _header = value; _columns = fields.Count;
                _timestampIndex = fields.FindIndex(x => string.Equals(x, "CaptureTimestamp", StringComparison.OrdinalIgnoreCase));
                _dateIndex = fields.FindIndex(x => string.Equals(x, "Date", StringComparison.OrdinalIgnoreCase));
                return;
            }
            if (fields == null || fields.Count != _columns) throw new InvalidDataException("CSV record/header column mismatch");
            string date = _byDate ? CaptureDate(fields) : "";
            Bucket bucket;
            if (!_buckets.TryGetValue(date, out bucket)) { bucket = new Bucket(); _buckets.Add(date, bucket); }
            if (bucket.Path == null || bucket.Count >= _maxRows)
            {
                if (bucket.Path != null) Close(bucket.Path);
                bucket.Part++; bucket.Count = 0;
                string suffix = (_byDate ? "_" + date : "") + "_" + bucket.Part.ToString("D3", CultureInfo.InvariantCulture);
                bucket.Path = Path.Combine(Path.GetDirectoryName(_basePath), Path.GetFileNameWithoutExtension(_basePath) + suffix + ".csv");
                // CreateNew prevents overwriting an existing run after a name collision.
                using (var writer = new StreamWriter(new FileStream(bucket.Path, FileMode.CreateNew, FileAccess.Write, FileShare.Read), Encoding)) writer.WriteLine(_header);
                _paths.Add(bucket.Path); _counts.Add(bucket.Path, 0);
            }
            Writer(bucket.Path).WriteLine(value); bucket.Count++; _counts[bucket.Path]++;
        }
        private string CaptureDate(List<string> fields)
        {
            string value = _timestampIndex >= 0 ? fields[_timestampIndex].Trim() : "";
            if (value.Length >= 10) value = value.Substring(0, 10);
            DateTime date;
            if (DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date)) return date.ToString("yyyy-MM-dd");
            value = _dateIndex >= 0 ? fields[_dateIndex].Trim() : "";
            return DateTime.TryParseExact(value, new[] { "yyyyMMdd", "yyyy-MM-dd" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out date) ? date.ToString("yyyy-MM-dd") : "unknown-date";
        }
        private StreamWriter Writer(string path)
        {
            OpenFile item;
            if (!_open.TryGetValue(path, out item))
            {
                if (_open.Count >= 8) Close(_open.OrderBy(x => x.Value.Access).First().Key);
                item = new OpenFile { Writer = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read), Encoding, 65536) };
                _open.Add(path, item);
            }
            item.Access = ++_clock; return item.Writer;
        }
        private void Close(string path) { OpenFile item; if (_open.TryGetValue(path, out item)) { item.Writer.Dispose(); _open.Remove(path); } }
        private void EnsureEmptyOutput()
        {
            if (_paths.Count > 0 || _header == null) return;
            using (var writer = new StreamWriter(new FileStream(_basePath, FileMode.CreateNew, FileAccess.Write, FileShare.Read), Encoding)) writer.WriteLine(_header);
            _paths.Add(_basePath); _counts.Add(_basePath, 0);
        }
        public override void Flush()
        {
            foreach (var item in _open.Values) item.Writer.Flush();
            EnsureEmptyOutput();
            if (_paths.Count == 0) return;
            string manifest = _paths[0] + ".parts.txt", temp = manifest + ".tmp";
            using (var writer = new StreamWriter(temp, false, Encoding))
            {
                writer.WriteLine("FileName,DataRows");
                foreach (var path in _paths) writer.WriteLine(ResultCsv.WriteRecord(new[] { Path.GetFileName(path), _counts[path].ToString(CultureInfo.InvariantCulture) }));
            }
            if (File.Exists(manifest)) File.Replace(temp, manifest, null); else File.Move(temp, manifest);
        }
        protected override void Dispose(bool disposing)
        {
            if (!_disposed && disposing) { try { Flush(); } finally { foreach (var path in _open.Keys.ToArray()) Close(path); _disposed = true; } }
            base.Dispose(disposing);
        }
        internal static IEnumerable<string> Expand(string firstPath)
        {
            string manifest = firstPath + ".parts.txt";
            if (!File.Exists(manifest)) return new[] { firstPath };
            var paths = new List<string>();
            using (var reader = new StreamReader(manifest, Encoding.UTF8, true))
            {
                ResultCsv.ReadHeader(reader); List<string> record;
                while ((record = ResultCsv.ReadRecord(reader)) != null)
                {
                    if (record.Count != 2 || record[0] != Path.GetFileName(record[0]) || !record[0].EndsWith(".csv", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Invalid split CSV manifest");
                    paths.Add(Path.Combine(Path.GetDirectoryName(firstPath), record[0]));
                }
            }
            if (paths.Count == 0) throw new InvalidDataException("Empty split CSV manifest");
            return paths;
        }
    }
}
