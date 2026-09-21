using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
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
            string runId;
            return ImportFiles(request, new[] { filePath }, progress, CancellationToken.None, out runId);
        }

        internal long ImportFiles(AgentHistoryFileImportRequest request, IEnumerable<string> filePaths, Action<long> progress, CancellationToken cancellation, out string runId, IDictionary<string,string> filePositions = null, ISet<string> excludedPositions = null)
        {
            runId = null;
            var paths = filePaths.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (paths.Length == 0) throw new InvalidDataException("선택한 CSV 파일이 없습니다.");
            foreach (string path in paths)
                if (!File.Exists(path) || !string.Equals(Path.GetExtension(path), ".csv", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("CSV 파일을 찾을 수 없습니다: " + path);
            if (request == null) request = new AgentHistoryFileImportRequest();
            string sourceName = string.Join("; ", paths.Select(Path.GetFileName));
            SqliteRunStore.RunStoreSession session = null;
            bool completed = false;
            long processed = 0;
            try
            {
                session = _store.Start(new SqliteRunStore.RunStoreStart
                {
                    SourceType = "csv-file-stream",
                    Mode = FirstNonEmpty(request.mode, "csv-analysis"),
                    SourceName = FirstNonEmpty(request.sourceName, sourceName),
                    AgentVersion = Program.AgentVersion,
                    WebVersion = request.webVersion ?? "",
                    NamingProfile = request.namingProfile,
                    NamingProfileJson = _json.Serialize(request.namingProfile ?? new NamingProfile()),
                    WorkspaceType = "csv-analysis",
                    WorkspaceName = FirstNonEmpty(request.sourceName, sourceName),
                    WorkspaceKey = "csv|" + FirstNonEmpty(request.sourceName, sourceName).Trim().ToLowerInvariant()
                });

                foreach (string filePath in paths)
                {
                cancellation.ThrowIfCancellationRequested();
                string filePosition=null;
                if(filePositions!=null)filePositions.TryGetValue(filePath,out filePosition);
                using (var reader = OpenCsvReader(filePath,cancellation))
                {
                    List<string> headers = ResultCsv.ReadHeader(reader);
                    CsvColumnMap columns = CsvColumnMap.Create(headers);
                    if (columns.CellId < 0 && columns.FullPath < 0) throw new InvalidDataException("CSV에서 Cell ID 또는 FullPath 열을 찾지 못했습니다.");

                    List<string> values;
                    int sourceRowNumber = 1;
                    while ((values = ResultCsv.ReadRecord(reader)) != null)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        sourceRowNumber++;
                        if (values.Count == 1 && values[0].Length == 0) continue;
                        if (values.Count != headers.Count) throw new InvalidDataException("CSV 열 수 불일치: " + sourceRowNumber);
                        if (values.All(string.IsNullOrWhiteSpace)) continue;
                        int originalRow;
                        if(!int.TryParse(columns.Value(values,columns.SourceRow),NumberStyles.Integer,CultureInfo.InvariantCulture,out originalRow)||originalRow<1)originalRow=sourceRowNumber;
                        string dateValue=columns.Value(values,columns.Date);
                        bool captureDateAllowed=columns.HasInspectionDate||dateValue.IndexOf('-')>=0||string.IsNullOrWhiteSpace(columns.Value(values,columns.FullPath));
                        var record = new AgentHistoryRecordRequest
                        {
                            sourceFileName = FirstNonEmpty(columns.Value(values,columns.SourceFile),Path.GetFileName(filePath)),
                            sourceRowNumber = originalRow,
                            fullPath = columns.Value(values, columns.FullPath),
                            processedPath = columns.Value(values, columns.ProcessedPath),
                            cellId = columns.Value(values, columns.CellId),
                            position = FirstNonEmpty(columns.Value(values, columns.Position), FirstNonEmpty(filePosition,request.defaultPosition)),
                            workspaceType = columns.Value(values, columns.WorkspaceType),
                            workspaceName = columns.Value(values, columns.WorkspaceName),
                            workspaceKey = columns.Value(values, columns.WorkspaceKey),
                            totalResult = columns.Value(values, columns.TotalResult),
                            judgement = columns.Value(values, columns.Judgement),
                            captureTimestamp = FirstNonEmpty(columns.Value(values, columns.CaptureTimestamp), captureDateAllowed?CaptureTimestamp(dateValue, columns.Value(values, columns.Time)):""),
                            tools = columns.ReadTools(values)
                        };
                        if(excludedPositions!=null && excludedPositions.Contains(record.position))continue;
                        _store.AppendImportedRecord(session, record);
                        processed++;
                        if (processed % 250 == 0) progress?.Invoke(processed);
                    }
                }
                }
                cancellation.ThrowIfCancellationRequested();
                _store.Complete(session, "completed", "대용량 CSV 스트리밍 저장 완료");
                runId = session.RunId;
                completed = true;
                progress?.Invoke(processed);
                return processed;
            }
            catch
            {
                if (session != null) _store.DiscardFailedImport(session);
                throw;
            }
            finally
            {
                if (!completed) progress?.Invoke(processed);
            }
        }

        private static StreamReader OpenCsvReader(string path,CancellationToken cancellation)
        {
            Encoding encoding=Encoding.UTF8;
            using(var stream=File.OpenRead(path))
            {
                var prefix=new byte[4];int count=stream.Read(prefix,0,prefix.Length);
                bool bom=(count>=3&&prefix[0]==0xef&&prefix[1]==0xbb&&prefix[2]==0xbf)||(count>=2&&((prefix[0]==0xff&&prefix[1]==0xfe)||(prefix[0]==0xfe&&prefix[1]==0xff)));
                if(!bom)
                {
                    stream.Position=0;
                    try{using(var check=new StreamReader(stream,new UTF8Encoding(false,true),false,131072,true))
                    {var buffer=new char[65536];while(check.Read(buffer,0,buffer.Length)>0)cancellation.ThrowIfCancellationRequested();}}
                    catch(DecoderFallbackException){encoding=Encoding.Default;}
                }
            }
            return new StreamReader(path,encoding,true,131072);
        }

        private static string CaptureTimestamp(string dateValue, string timeValue)
        {
            string date = (dateValue ?? "").Trim();
            string time = (timeValue ?? "").Trim();
            DateTime parsedDate;
            DateTime parsedTime;
            // Aggregated Cell exports have a known date but no single observation time.
            if (time.Length == 0 && DateTime.TryParseExact(date, new[] { "yyyyMMdd", "yyyy-MM-dd", "yyyy/MM/dd" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedDate))
                return parsedDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
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
            internal int SourceFile=-1,SourceRow=-1;
            internal bool HasInspectionDate;
            internal int CellId = -1, FullPath = -1, ProcessedPath = -1, Position = -1, WorkspaceType = -1, WorkspaceName = -1, WorkspaceKey = -1, TotalResult = -1, Judgement = -1, CaptureTimestamp = -1, Date = -1, Time = -1;
            internal readonly List<ToolColumn> Tools = new List<ToolColumn>();

            internal static CsvColumnMap Create(List<string> headers)
            {
                var map = new CsvColumnMap();
                var toolMap = new Dictionary<string, ToolColumn>(StringComparer.OrdinalIgnoreCase);
                for (int index = 0; index < (headers ?? new List<string>()).Count; index++)
                {
                    string header = (headers[index] ?? "").Trim();
                    string key = Normalize(header);
                    if(key=="sourcefile"||key=="sourcefilename"){map.SourceFile=index;continue;}
                    if(key=="sourcerow"||key=="sourcerownumber"){map.SourceRow=index;continue;}
                    if(key=="inspectiondate"){map.HasInspectionDate=true;continue;}
                    if (map.CellId < 0 && (key == "cellid" || key == "cell" || key == "id")) { map.CellId = index; continue; }
                    if (map.FullPath < 0 && (key == "fullpath" || key == "imagepath" || key == "filepath" || key == "sourceimagepath" || key == "sourcefilepath")) { map.FullPath = index; continue; }
                    if (map.ProcessedPath < 0 && (key == "processedpath" || key == "processingpath" || key == "croppath")) { map.ProcessedPath = index; continue; }
                    if (map.Position < 0 && (key == "position" || key == "positionkey")) { map.Position = index; continue; }
                    if (map.WorkspaceType < 0 && (key == "workspacetype" || key == "inspectionmode" || key == "mode")) { map.WorkspaceType = index; continue; }
                    if (map.WorkspaceName < 0 && key == "workspacename") { map.WorkspaceName = index; continue; }
                    if (map.WorkspaceKey < 0 && key == "workspacekey") { map.WorkspaceKey = index; continue; }
                    if (map.TotalResult < 0 && (key == "totalresult" || key == "result" || key == "total")) { map.TotalResult = index; continue; }
                    if (map.Judgement < 0 && (key == "judgement" || key == "judgment")) { map.Judgement = index; continue; }
                    if (map.CaptureTimestamp < 0 && (key == "capturetimestamp" || key == "capturedatetime")) { map.CaptureTimestamp = index; continue; }
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
                    double? parsedScore = ResultCsv.ParseScore(scoreText);
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
