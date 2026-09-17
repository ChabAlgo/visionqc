using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace VisionQC.LocalAgent.Services
{
    // Shared streaming codec: quoted commas/newlines are data, never row boundaries.
    internal static class ResultCsv
    {
        internal static List<string> ReadRecord(TextReader reader)
        {
            var fields = new List<string>();
            var field = new StringBuilder();
            bool quoted = false, closed = false, started = false;
            while (true)
            {
                int next = reader.Read();
                if (next < 0)
                {
                    if (quoted) throw new InvalidDataException("CSV 따옴표가 닫히지 않았습니다.");
                    if (!started) return null;
                    fields.Add(field.ToString()); return fields;
                }
                started = true;
                char c = (char)next;
                if (quoted)
                {
                    if (c != '"') field.Append(c);
                    else if (reader.Peek() == '"') { reader.Read(); field.Append('"'); }
                    else { quoted = false; closed = true; }
                    continue;
                }
                if (c == ',' || c == '\r' || c == '\n')
                {
                    fields.Add(field.ToString()); field.Clear(); closed = false;
                    if (c == ',') continue;
                    if (c == '\r' && reader.Peek() == '\n') reader.Read();
                    return fields;
                }
                if (closed) throw new InvalidDataException("CSV 닫는 따옴표 뒤에 잘못된 문자가 있습니다.");
                if (c == '"')
                {
                    if (field.Length != 0) throw new InvalidDataException("CSV 필드 안의 따옴표가 잘못되었습니다.");
                    quoted = true;
                }
                else field.Append(c);
            }
        }

        internal static string WriteRecord(IEnumerable<string> fields)
        {
            return string.Join(",", fields.Select(value => {
                string text = value ?? "";
                return text.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0 ? text : "\"" + text.Replace("\"", "\"\"") + "\"";
            }));
        }

        internal static List<string> ReadHeader(TextReader reader)
        {
            var header = ReadRecord(reader);
            if (header == null || header.Any(string.IsNullOrWhiteSpace)) throw new InvalidDataException("CSV 헤더가 비어 있습니다.");
            header = header.Select(x => x.Trim().TrimStart('\uFEFF')).ToList();
            if (header.Distinct(StringComparer.OrdinalIgnoreCase).Count() != header.Count) throw new InvalidDataException("CSV 열 이름이 중복되었습니다.");
            return header;
        }

        internal static double? ParseScore(string text)
        {
            text = (text ?? "").Trim();
            if (text.Length == 0) return null;
            if (text.IndexOf('.') < 0 && text.Count(c => c == ',') == 1) text = text.Replace(',', '.');
            double value;
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) || double.IsNaN(value) || double.IsInfinity(value) || value < 0 || value > 1)
                throw new InvalidDataException("유효하지 않은 Score: " + text);
            return value;
        }

        internal static string Merge(string outputRoot, IEnumerable<string> paths)
        {
            var inputs = paths.ToList();
            if (inputs.Count == 0) return "";
            if (inputs.Any(string.IsNullOrWhiteSpace)) throw new InvalidDataException("Position 결과 CSV 경로가 비어 있습니다.");
            // Missing worker outputs must not silently disappear from a completed run.
            foreach (var input in inputs) if (!File.Exists(input)) throw new FileNotFoundException("Position 결과 CSV가 없습니다.", input);
            if (inputs.Count == 1) return inputs[0];
            var headers = new List<List<string>>();
            var union = new List<string>();
            foreach (string input in inputs)
                using (var reader = new StreamReader(input, Encoding.Default, true))
                {
                    var header = ReadHeader(reader); headers.Add(header);
                    foreach (string column in header) if (!union.Contains(column, StringComparer.OrdinalIgnoreCase)) union.Add(column);
                }
            string output = Path.Combine(outputRoot, "results_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture) + "_parallel.csv");
            string temporary = output + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var writer = new StreamWriter(temporary, false, new UTF8Encoding(true)))
                {
                    writer.WriteLine(WriteRecord(union));
                    for (int i = 0; i < inputs.Count; i++)
                        using (var reader = new StreamReader(inputs[i], Encoding.Default, true))
                        {
                            var header = ReadHeader(reader);
                            if (!header.SequenceEqual(headers[i])) throw new InvalidDataException("병합 중 CSV 헤더가 변경되었습니다.");
                            var map = header.Select(column => union.FindIndex(x => string.Equals(x, column, StringComparison.OrdinalIgnoreCase))).ToArray();
                            List<string> record;
                            while ((record = ReadRecord(reader)) != null)
                            {
                                if (record.Count == 1 && record[0].Length == 0) continue;
                                if (record.Count != header.Count) throw new InvalidDataException("CSV 열 수 불일치: " + inputs[i]);
                                var values = new string[union.Count];
                                for (int j = 0; j < map.Length; j++) values[map[j]] = record[j];
                                writer.WriteLine(WriteRecord(values));
                            }
                        }
                }
                File.Move(temporary, output);
                return output;
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
