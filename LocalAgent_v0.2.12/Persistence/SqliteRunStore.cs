using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using VisionQC.LocalAgent.Domain;
using VisionQC.LocalAgent.Services;
using VpdlGreenHeatmapOverlay;

namespace VisionQC.LocalAgent.Persistence
{
    // SQLite는 원본 이미지를 복사하지 않고 검사 결과와 원본/Overlay 경로만 영구 보관한다.
    internal sealed class SqliteRunStore : IDisposable
    {
        internal const int CommitBatchSize = 200;
        private readonly object _schemaSync = new object();
        private readonly string _databasePath;
        private bool _schemaReady;

        internal SqliteRunStore(string databasePath)
        {
            _databasePath = databasePath;
        }

        internal string DatabasePath { get { return _databasePath; } }

        internal RunStoreSession Start(RunStoreStart request)
        {
            EnsureSchema();
            var session = new RunStoreSession
            {
                RunId = Guid.NewGuid().ToString("N"),
                NamingProfile = request == null ? null : request.NamingProfile,
                Connection = new SQLiteConnection("Data Source=" + _databasePath + ";Version=3;Foreign Keys=True;")
            };
            session.Connection.Open();
            session.Transaction = session.Connection.BeginTransaction();
            using (var command = session.Connection.CreateCommand())
            {
                command.Transaction = session.Transaction;
                command.CommandText = @"INSERT INTO runs (run_id, source_type, mode, source_name, started_at_utc, status, agent_version, web_version, output_root, config_json, naming_profile_json)
VALUES (@run_id, @source_type, @mode, @source_name, @started_at_utc, 'running', @agent_version, @web_version, @output_root, @config_json, @naming_profile_json);";
                Add(command, "@run_id", session.RunId);
                Add(command, "@source_type", request == null ? "unknown" : request.SourceType);
                Add(command, "@mode", request == null ? "" : request.Mode);
                Add(command, "@source_name", request == null ? "" : request.SourceName);
                Add(command, "@started_at_utc", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
                Add(command, "@agent_version", request == null ? "" : request.AgentVersion);
                Add(command, "@web_version", request == null ? "" : request.WebVersion);
                Add(command, "@output_root", request == null ? "" : request.OutputRoot);
                Add(command, "@config_json", request == null ? "" : request.ConfigJson);
                Add(command, "@naming_profile_json", request == null ? "" : request.NamingProfileJson);
                command.ExecuteNonQuery();
            }
            CommitAndContinue(session);
            return session;
        }

        internal void AppendLiveRecord(RunStoreSession session, LiveAnalysisRecord record)
        {
            if (record == null) return;
            var tools = new List<HistoryToolValue>();
            foreach (LiveToolResult tool in record.Tools.Values)
                tools.Add(new HistoryToolValue { Tool = tool.Tool, Result = tool.Result, Score = tool.Score, OverlayPath = tool.OverlayPath });
            Append(session, new HistoryRecordValue
            {
                SourceFileName = record.FileName,
                FullPath = record.FullPath,
                CellId = record.CellId,
                Position = record.Position,
                TotalResult = record.TotalResult,
                Judgement = record.Judgement,
                Tools = tools
            });
        }

        internal void AppendImportedRecord(RunStoreSession session, AgentHistoryRecordRequest record)
        {
            if (record == null) return;
            var tools = new List<HistoryToolValue>();
            foreach (AgentHistoryToolResultRequest tool in record.tools ?? new List<AgentHistoryToolResultRequest>())
            {
                if (tool != null) tools.Add(new HistoryToolValue { Tool = tool.tool, Result = tool.result, Score = tool.score, OverlayPath = tool.overlayPath });
            }
            Append(session, new HistoryRecordValue
            {
                SourceFileName = record.sourceFileName,
                SourceRowNumber = record.sourceRowNumber,
                FullPath = record.fullPath,
                CellId = record.cellId,
                Position = record.position,
                TotalResult = record.totalResult,
                Judgement = record.judgement,
                Tools = tools
            });
        }

        internal void Complete(RunStoreSession session, string status, string message)
        {
            if (session == null || session.Closed) return;
            try
            {
                CommitAndContinue(session);
                using (var command = session.Connection.CreateCommand())
                {
                    command.Transaction = session.Transaction;
                    command.CommandText = "UPDATE runs SET ended_at_utc=@ended_at_utc, status=@status, message=@message, record_count=@record_count WHERE run_id=@run_id;";
                    Add(command, "@ended_at_utc", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
                    Add(command, "@status", status);
                    Add(command, "@message", message);
                    Add(command, "@record_count", session.RecordCount);
                    Add(command, "@run_id", session.RunId);
                    command.ExecuteNonQuery();
                }
                session.Transaction.Commit();
            }
            finally
            {
                session.Closed = true;
                try { session.Transaction.Dispose(); } catch { }
                try { session.Connection.Dispose(); } catch { }
            }
        }

        private void Append(RunStoreSession session, HistoryRecordValue record)
        {
            if (session == null || session.Closed) throw new InvalidOperationException("닫힌 SQLite 기록 세션입니다.");
            NamingParseResult parsed = null;
            try
            {
                if (session.NamingProfile != null && !string.IsNullOrWhiteSpace(record.FullPath))
                    parsed = NamingProfileParser.Parse(session.NamingProfile, record.FullPath);
            }
            catch { }
            string cellId = FirstNonEmpty(parsed == null ? "" : parsed.cellId, record.CellId);
            string captureTimestamp = parsed == null ? "" : (parsed.captureTimestamp ?? "");
            long imageId;
            using (var command = session.Connection.CreateCommand())
            {
                command.Transaction = session.Transaction;
                command.CommandText = @"INSERT INTO images (run_id, sequence_no, source_file_name, source_row_number, full_path, cell_id, position_key, total_result, judgement, capture_timestamp, inspected_at_utc)
VALUES (@run_id, @sequence_no, @source_file_name, @source_row_number, @full_path, @cell_id, @position_key, @total_result, @judgement, @capture_timestamp, @inspected_at_utc);
SELECT last_insert_rowid();";
                Add(command, "@run_id", session.RunId);
                Add(command, "@sequence_no", session.RecordCount + 1);
                Add(command, "@source_file_name", record.SourceFileName);
                Add(command, "@source_row_number", record.SourceRowNumber);
                Add(command, "@full_path", record.FullPath);
                Add(command, "@cell_id", cellId);
                Add(command, "@position_key", record.Position);
                Add(command, "@total_result", record.TotalResult);
                Add(command, "@judgement", record.Judgement);
                Add(command, "@capture_timestamp", captureTimestamp);
                Add(command, "@inspected_at_utc", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
                imageId = Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
            }
            foreach (HistoryToolValue tool in record.Tools ?? new List<HistoryToolValue>())
            {
                if (tool == null || string.IsNullOrWhiteSpace(tool.Tool)) continue;
                using (var command = session.Connection.CreateCommand())
                {
                    command.Transaction = session.Transaction;
                    command.CommandText = "INSERT INTO tool_results (run_id, image_id, tool_name, result, score, overlay_path) VALUES (@run_id, @image_id, @tool_name, @result, @score, @overlay_path);";
                    Add(command, "@run_id", session.RunId);
                    Add(command, "@image_id", imageId);
                    Add(command, "@tool_name", tool.Tool);
                    Add(command, "@result", tool.Result);
                    Add(command, "@score", tool.Score.HasValue ? (object)tool.Score.Value : DBNull.Value);
                    Add(command, "@overlay_path", tool.OverlayPath);
                    command.ExecuteNonQuery();
                }
            }
            session.RecordCount++;
            session.PendingCount++;
            if (session.PendingCount >= CommitBatchSize) CommitAndContinue(session);
        }

        private void EnsureSchema()
        {
            lock (_schemaSync)
            {
                if (_schemaReady) return;
                string directory = Path.GetDirectoryName(_databasePath);
                if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
                using (var connection = new SQLiteConnection("Data Source=" + _databasePath + ";Version=3;Foreign Keys=True;"))
                {
                    connection.Open();
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = @"PRAGMA journal_mode=WAL;
PRAGMA synchronous=NORMAL;
CREATE TABLE IF NOT EXISTS schema_info (schema_version INTEGER NOT NULL);
INSERT INTO schema_info (schema_version) SELECT 1 WHERE NOT EXISTS (SELECT 1 FROM schema_info);
CREATE TABLE IF NOT EXISTS runs (
  run_id TEXT PRIMARY KEY, source_type TEXT NOT NULL, mode TEXT, source_name TEXT, started_at_utc TEXT NOT NULL, ended_at_utc TEXT,
  status TEXT NOT NULL, message TEXT, record_count INTEGER NOT NULL DEFAULT 0, agent_version TEXT, web_version TEXT,
  output_root TEXT, config_json TEXT, naming_profile_json TEXT
);
CREATE TABLE IF NOT EXISTS images (
  image_id INTEGER PRIMARY KEY AUTOINCREMENT, run_id TEXT NOT NULL, sequence_no INTEGER NOT NULL, source_file_name TEXT,
  source_row_number INTEGER NOT NULL DEFAULT 0, full_path TEXT, cell_id TEXT, position_key TEXT, total_result TEXT,
  judgement TEXT, capture_timestamp TEXT, inspected_at_utc TEXT NOT NULL,
  FOREIGN KEY(run_id) REFERENCES runs(run_id)
);
CREATE TABLE IF NOT EXISTS tool_results (
  tool_result_id INTEGER PRIMARY KEY AUTOINCREMENT, run_id TEXT NOT NULL, image_id INTEGER NOT NULL, tool_name TEXT NOT NULL,
  result TEXT, score REAL, overlay_path TEXT,
  FOREIGN KEY(run_id) REFERENCES runs(run_id), FOREIGN KEY(image_id) REFERENCES images(image_id)
);
CREATE INDEX IF NOT EXISTS idx_images_run_sequence ON images(run_id, sequence_no);
CREATE INDEX IF NOT EXISTS idx_images_cell_capture ON images(cell_id, capture_timestamp);
CREATE INDEX IF NOT EXISTS idx_images_full_path ON images(full_path);
CREATE INDEX IF NOT EXISTS idx_tool_results_run_tool ON tool_results(run_id, tool_name);";
                        command.ExecuteNonQuery();
                    }
                }
                _schemaReady = true;
            }
        }

        private static void CommitAndContinue(RunStoreSession session)
        {
            if (session.Transaction != null) session.Transaction.Commit();
            session.Transaction = session.Connection.BeginTransaction();
            session.PendingCount = 0;
        }

        private static void Add(SQLiteCommand command, string name, object value)
        {
            command.Parameters.AddWithValue(name, value ?? "");
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (string value in values) if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
            return "";
        }

        public void Dispose() { }

        internal sealed class RunStoreSession
        {
            internal string RunId;
            internal NamingProfile NamingProfile;
            internal SQLiteConnection Connection;
            internal SQLiteTransaction Transaction;
            internal int RecordCount;
            internal int PendingCount;
            internal bool Closed;
        }

        internal sealed class RunStoreStart
        {
            internal string SourceType;
            internal string Mode;
            internal string SourceName;
            internal string AgentVersion;
            internal string WebVersion;
            internal string OutputRoot;
            internal string ConfigJson;
            internal string NamingProfileJson;
            internal NamingProfile NamingProfile;
        }

        private sealed class HistoryRecordValue
        {
            internal string SourceFileName;
            internal int SourceRowNumber;
            internal string FullPath;
            internal string CellId;
            internal string Position;
            internal string TotalResult;
            internal string Judgement;
            internal List<HistoryToolValue> Tools;
        }

        private sealed class HistoryToolValue
        {
            internal string Tool;
            internal string Result;
            internal double? Score;
            internal string OverlayPath;
        }
    }
}
