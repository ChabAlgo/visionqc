using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using VisionQC.LocalAgent.Domain;
using VisionQC.LocalAgent.Persistence;

namespace VisionQC.LocalAgent.Services
{
    // CSV를 한 행씩 읽어 SQLite에 바로 넣는다. 대용량 파일의 전체 행/이미지 경로 목록을 메모리에 만들지 않는다.
    internal sealed class CsvHistoryFileImporter
    {
        private readonly SqliteRunStore _store;
        private readonly JavaScriptSerializer _json;

        internal CsvHistoryFileImporter(SqliteRunStore store, JavaScriptSerializer json)
        {
            _store = store;
            _json = json;
        }

        internal long Import(AgentHistoryFileImportRequest request, string filePath, Action<long> progress)
        {
            if (request == null) request = new AgentHistoryFileImportRequest();
            SqliteRunStore.RunStoreSession session = null;
            bool completed = false;
            long processed = 0;
            try
            {
                session = _store.Start(new SqliteRunStore.RunStoreStart
                {
                    SourceType = "csv-file-stream",
                    Mode = FirstNonEmpty(request.mode, "csv-analysis"),
                    SourceName = FirstNonEmpty(request.sourceName, Path.GetFileName(filePath)),
                    AgentVersion = Program.AgentVersion,
                    WebVersion = request.webVersion ?? "",
                    NamingProfile = request.namingProfile,
                    NamingProfileJson = _json.Serialize(request.namingProfile ?? new NamingProfile()),
                    WorkspaceType = "csv-analysis",
                    WorkspaceName = FirstNonEmpty(request.sourceName, Path.GetFileName(filePath)),
                    WorkspaceKey = "csv|" + FirstNonEmpty(request.sourceName, Path.GetFileName(filePath)).Trim().ToLowerInvariant()
                });

                using (var reader = new StreamReader(filePath, Encoding.Default, true, 1024 * 128))
                {
                    string headerLine = reader.ReadLine();
                    if (headerLine == null) throw new InvalidDataException("CSV 헤더가 없습니다.");
                    List<string> headers = ParseCsvLine(headerLine);
                    CsvColumnMap columns = CsvColumnMap.Create(headers);
                    if (columns.CellId < 0 && columns.FullPath < 0) throw new InvalidDataException("CSV에서 Cell ID 또는 FullPath 열을 찾지 못했습니다.");

                    string line;
                    int sourceRowNumber = 1;
                    while ((line = reader.ReadLine()) != null)
                    {
                        sourceRowNumber++;
                        if (line.Length == 0) continue;
                        List<string> values = ParseCsvLine(line);
                        if (values.All(string.IsNullOrWhiteSpace)) continue;
                        var record = new AgentHistoryRecordRequest
                        {
                            sourceFileName = Path.GetFileName(filePath),
                            sourceRowNumber = sourceRowNumber,
                            fullPath = columns.Value(values, columns.FullPath),
                            cellId = columns.Value(values, columns.CellId),
                            position = FirstNonEmpty(columns.Value(values, columns.Position), request.defaultPosition),
                            totalResult = columns.Value(values, columns.TotalResult),
                            judgement = columns.Value(values, columns.Judgement),
                            captureTimestamp = CaptureTimestamp(columns.Value(values, columns.Date), columns.Value(values, columns.Time)),
                            tools = columns.ReadTools(values)
                        };
                        _store.AppendImportedRecord(session, record);
                        processed++;
                        if (processed % 250 == 0) progress?.Invoke(processed);
                    }
                }
                _store.Complete(session, "completed", "대용량 CSV 스트리밍 저장 완료");
                completed = true;
                progress?.Invoke(processed);
                return processed;
            }
            catch
            {
                if (session != null && !session.Closed) _store.Complete(session, "failed", "대용량 CSV 스트리밍 저장 실패");
                throw;
            }
            finally
            {
                if (!completed) progress?.Invoke(processed);
            }
        }

        private static string CaptureTimestamp(string dateValue, string timeValue)
        {
            string date = (dateValue ?? "").Trim();
            string time = (timeValue ?? "").Trim();
            DateTime parsedDate;
            DateTime parsedTime;
            if (DateTime.TryParseExact(date, new[] { "yyyyMMdd", "yyyy-MM-dd", "yyyy/MM/dd" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedDate) &&
                DateTime.TryParseExact(time, new[] { "HHmmss", "HH:mm:ss", "HH:mm" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedTime))
                return parsedDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "T" + parsedTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            return "";
        }

        private static List<string> ParseCsvLine(string line)
        {
            var values = new List<string>();
            var value = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < (line ?? "").Length; i++)
            {
                char c = line[i];
                if (c == '"')
                {
                    if (quoted && i + 1 < line.Length && line[i + 1] == '"') { value.Append('"'); i++; }
                    else quoted = !quoted;
                }
                else if (c == ',' && !quoted) { values.Add(value.ToString().Trim()); value.Length = 0; }
                else value.Append(c);
            }
            values.Add(value.ToString().Trim());
            return values;
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (string value in values) if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
            return "";
        }

        private sealed class CsvColumnMap
        {
            internal int CellId = -1, FullPath = -1, Position = -1, TotalResult = -1, Judgement = -1, Date = -1, Time = -1;
            internal readonly List<ToolColumn> Tools = new List<ToolColumn>();

            internal static CsvColumnMap Create(List<string> headers)
            {
                var map = new CsvColumnMap();
                var toolMap = new Dictionary<string, ToolColumn>(StringComparer.OrdinalIgnoreCase);
                for (int index = 0; index < (headers ?? new List<string>()).Count; index++)
                {
                    string header = (headers[index] ?? "").Trim();
                    string key = Normalize(header);
                    if (map.CellId < 0 && (key == "cellid" || key == "cell" || key == "id")) { map.CellId = index; continue; }
                    if (map.FullPath < 0 && (key == "fullpath" || key == "imagepath" || key == "filepath" || key == "sourceimagepath" || key == "sourcefilepath")) { map.FullPath = index; continue; }
                    if (map.Position < 0 && (key == "position" || key == "positionkey")) { map.Position = index; continue; }
                    if (map.TotalResult < 0 && (key == "totalresult" || key == "result" || key == "total")) { map.TotalResult = index; continue; }
                    if (map.Judgement < 0 && (key == "judgement" || key == "judgment")) { map.Judgement = index; continue; }
                    if (map.Date < 0 && (key == "date" || key == "capturedate")) { map.Date = index; continue; }
                    if (map.Time < 0 && (key == "time" || key == "capturetime")) { map.Time = index; continue; }

                    string toolName;
                    bool isScore;
                    if (!TryToolColumn(header, out toolName, out isScore)) continue;
                    ToolColumn tool;
                    if (!toolMap.TryGetValue(toolName, out tool)) { tool = new ToolColumn { Name = toolName }; toolMap[toolName] = tool; map.Tools.Add(tool); }
                    if (isScore) tool.Score = index; else tool.Result = index;
                }
                return map;
            }

            internal string Value(List<string> values, int index)
            {
                return index >= 0 && index < values.Count ? (values[index] ?? "").Trim() : "";
            }

            internal List<AgentHistoryToolResultRequest> ReadTools(List<string> values)
            {
                var output = new List<AgentHistoryToolResultRequest>();
                foreach (ToolColumn column in Tools)
                {
                    string result = Value(values, column.Result);
                    string scoreText = Value(values, column.Score);
                    double score;
                    double? parsedScore = double.TryParse(scoreText, NumberStyles.Float, CultureInfo.InvariantCulture, out score) ? (double?)score : null;
                    if (string.IsNullOrWhiteSpace(result) && !parsedScore.HasValue) continue;
                    output.Add(new AgentHistoryToolResultRequest { tool = column.Name, result = result, score = parsedScore });
                }
                return output;
            }

            private static string Normalize(string value)
            {
                return new string((value ?? "").Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
            }

            private static bool TryToolColumn(string header, out string toolName, out bool isScore)
            {
                string text = (header ?? "").Trim();
                toolName = "";
                isScore = false;
                int split = text.LastIndexOf('_');
                if (split < 1) split = text.LastIndexOf(' ');
                if (split < 1) return false;
                string suffix = Normalize(text.Substring(split + 1));
                if (suffix != "result" && suffix != "score") return false;
                toolName = text.Substring(0, split).Trim();
                if (string.IsNullOrWhiteSpace(toolName) || string.Equals(Normalize(toolName), "total", StringComparison.OrdinalIgnoreCase)) return false;
                isScore = suffix == "score";
                return true;
            }

            internal sealed class ToolColumn
            {
                internal string Name;
                internal int Result = -1;
                internal int Score = -1;
            }
        }
    }
}
