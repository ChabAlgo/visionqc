using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using VisionQC.LocalAgent.Services;

namespace VisionQC.LocalAgent.Persistence
{
    // Disk-backed, disposable projection. Raw observations stay in the run store.
    // A cursor and all derived rows commit together; retrying a batch cannot count it twice.
    internal sealed class AnalysisProjection : IDisposable
    {
        private readonly SQLiteConnection _connection;
        private readonly object _sync = new object();
        private readonly string _runId;
        internal Action<string,double> Timing;
        internal AnalysisProjection(string sourcePath, string cachePath, string runId)
        {
            if (string.IsNullOrWhiteSpace(runId)) throw new ArgumentException("runId");
            if (!File.Exists(sourcePath)) throw new FileNotFoundException("Analysis source is missing", sourcePath);
            _runId = runId;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(cachePath)));
            _connection = new SQLiteConnection("Data Source=" + cachePath + ";Version=3;");
            try
            {
            _connection.Open();
            using (var command = _connection.CreateCommand())
            {
                command.CommandText = "ATTACH DATABASE @path AS source;";
                command.Parameters.AddWithValue("@path", sourcePath); command.ExecuteNonQuery();
            }
            Execute(@"PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA cache_size=-32768;
PRAGMA source.cache_size=-16384; PRAGMA temp_store=FILE;
CREATE TABLE IF NOT EXISTS meta(run_id TEXT PRIMARY KEY, cursor INTEGER NOT NULL);
CREATE TABLE IF NOT EXISTS cells(day TEXT NOT NULL, cell TEXT NOT NULL, position TEXT NOT NULL,
 rows INTEGER NOT NULL, base_ng INTEGER NOT NULL, base_ok INTEGER NOT NULL, ng INTEGER NOT NULL DEFAULT 0,
 PRIMARY KEY(day,cell,position)) WITHOUT ROWID;
CREATE TABLE IF NOT EXISTS tools(day TEXT NOT NULL, cell TEXT NOT NULL, position TEXT NOT NULL, tool TEXT NOT NULL,
 base_ng INTEGER NOT NULL, base_ok INTEGER NOT NULL, min_score REAL, min_ng REAL, max_ng REAL, min_ok REAL, ng INTEGER NOT NULL DEFAULT 0,
 PRIMARY KEY(day,cell,position,tool)) WITHOUT ROWID;
CREATE TABLE IF NOT EXISTS thresholds(position TEXT NOT NULL,tool TEXT NOT NULL,value REAL NOT NULL,PRIMARY KEY(position,tool)) WITHOUT ROWID;
CREATE TABLE IF NOT EXISTS actual_ng(day TEXT NOT NULL,cell TEXT NOT NULL,position TEXT NOT NULL,path TEXT NOT NULL,wildcard INTEGER NOT NULL,
 PRIMARY KEY(day,cell,position,path,wildcard)) WITHOUT ROWID;
CREATE INDEX IF NOT EXISTS actual_ng_cell ON actual_ng(position,cell,day,wildcard);
CREATE INDEX IF NOT EXISTS cells_position ON cells(position,day,cell);
CREATE INDEX IF NOT EXISTS cells_actual ON cells(position,cell,day);
CREATE INDEX IF NOT EXISTS tools_position ON tools(position,tool,day,cell);
CREATE TABLE IF NOT EXISTS position_stats(day TEXT,position TEXT,total INTEGER,ng INTEGER,raw_rows INTEGER,duplicates INTEGER,PRIMARY KEY(day,position)) WITHOUT ROWID;
CREATE TABLE IF NOT EXISTS cell_stats(day TEXT PRIMARY KEY,total INTEGER,ng INTEGER) WITHOUT ROWID;
CREATE TABLE IF NOT EXISTS tool_stats(day TEXT,position TEXT,tool TEXT,total INTEGER,ng INTEGER,PRIMARY KEY(day,position,tool)) WITHOUT ROWID;
CREATE TABLE IF NOT EXISTS unknown_stats(day TEXT,position TEXT,count INTEGER,PRIMARY KEY(day,position)) WITHOUT ROWID;
CREATE TABLE IF NOT EXISTS workspace_refs(position TEXT,workspace_name TEXT,workspace_key TEXT,PRIMARY KEY(position,workspace_name,workspace_key)) WITHOUT ROWID;
CREATE TABLE IF NOT EXISTS stats_ready(id INTEGER PRIMARY KEY);
CREATE TEMP TABLE affected(day TEXT,cell TEXT,position TEXT,PRIMARY KEY(day,cell,position)) WITHOUT ROWID;");
            using (var command = Command("SELECT run_id FROM meta LIMIT 1"))
            {
                var existing = command.ExecuteScalar();
                if (existing != null && Convert.ToString(existing) != _runId) throw new InvalidDataException("Analysis cache belongs to another run");
            }
            using (var command = Command("INSERT OR IGNORE INTO meta VALUES(@run,0)")) command.ExecuteNonQuery();
            using (var command = Command("SELECT COUNT(*) FROM stats_ready WHERE id=2")) if (Convert.ToInt64(command.ExecuteScalar()) == 0)
            {
                using (var tx = _connection.BeginTransaction(System.Data.IsolationLevel.ReadCommitted)) using (var initialize = Command(RebuildCountersSql()+@"
INSERT OR IGNORE INTO workspace_refs SELECT position_key,COALESCE(workspace_name,''),COALESCE(workspace_key,'') FROM source.images WHERE run_id=@run AND image_id<=(SELECT cursor FROM meta WHERE run_id=@run) GROUP BY 1,2,3;
INSERT INTO stats_ready VALUES(2);"))
                { initialize.Transaction=tx; initialize.ExecuteNonQuery(); tx.Commit(); }
            }
            }
            catch { _connection.Dispose(); throw; }
        }

        private SQLiteCommand Command(string sql)
        {
            var command = _connection.CreateCommand(); command.CommandText = sql;
            command.Parameters.AddWithValue("@run", _runId); return command;
        }
        private void Execute(string sql) { using (var command = Command(sql)) command.ExecuteNonQuery(); }
        internal long Cursor { get { lock (_sync) using (var c = Command("SELECT cursor FROM meta WHERE run_id=@run")) return Convert.ToInt64(c.ExecuteScalar()); } }

        // LIMIT bounds each source snapshot even after a long disconnect. Cancellation rolls back its cursor.
        internal bool Advance(CancellationToken cancellation, int batchSize = 2000)
        {
            lock (_sync)
            {
                cancellation.ThrowIfCancellationRequested();
                using (var tx = _connection.BeginTransaction(System.Data.IsolationLevel.ReadCommitted))
                using (var command = Command(@"SELECT MAX(image_id) FROM (SELECT image_id FROM source.images
WHERE run_id=@run AND image_id>@after ORDER BY image_id LIMIT @limit)"))
                {
                    using (cancellation.Register(() => command.Cancel()))
                    {
                    var phase=Stopwatch.StartNew();
                    Action<string> elapsed=label=>{if(Timing!=null) Timing(label,phase.Elapsed.TotalMilliseconds);phase.Restart();};
                    command.Transaction = tx;
                    long after = Cursor;
                    command.Parameters.AddWithValue("@after", after);
                    command.Parameters.AddWithValue("@limit", Math.Max(1, Math.Min(10000, batchSize)));
                    var end = command.ExecuteScalar();
                    if (end == null || end == DBNull.Value) { tx.Commit(); return false; }
                    elapsed("source-cursor");
                    long next = Convert.ToInt64(end); command.Parameters.AddWithValue("@next", next);
                    // Capture date is already normalized by the importer; unknown stays a separate bucket.
                    const string day = "substr(COALESCE(i.capture_timestamp,''),1,10)";
                    string rows = " FROM source.images i WHERE i.run_id=@run AND i.image_id>@after AND i.image_id<=@next";
                    command.CommandText = "DELETE FROM affected; INSERT INTO affected SELECT " + day + ",i.cell_id,i.position_key" + rows + " GROUP BY 1,2,3;";
                    command.ExecuteNonQuery();
                    elapsed("affected");
                    command.CommandText = CountersSql(-1,true); command.ExecuteNonQuery(); elapsed("subtract-counters");
                    command.CommandText = "INSERT INTO cells(day,cell,position,rows,base_ng,base_ok) SELECT " + day + @",i.cell_id,i.position_key,COUNT(*),MAX(i.total_result='NG'),MAX(i.total_result='OK')" + rows + @" GROUP BY 1,2,3
ON CONFLICT(day,cell,position) DO UPDATE SET rows=rows+excluded.rows,base_ng=MAX(base_ng,excluded.base_ng),base_ok=MAX(base_ok,excluded.base_ok);";
                    command.ExecuteNonQuery();
                    elapsed("cell-facts");
                    command.CommandText = "INSERT INTO tools(day,cell,position,tool,base_ng,base_ok,min_score,min_ng,max_ng,min_ok) SELECT " + day + @",i.cell_id,i.position_key,t.tool_name,
MAX(t.result='NG'),MAX(t.result='OK'),MIN(t.score),MIN(CASE WHEN t.result='NG' THEN t.score END),MAX(CASE WHEN t.result='NG' THEN t.score END),MIN(CASE WHEN t.result='OK' THEN t.score END)
FROM source.images i JOIN source.tool_results t ON t.image_id=i.image_id
WHERE i.run_id=@run AND i.image_id>@after AND i.image_id<=@next GROUP BY 1,2,3,4
ON CONFLICT(day,cell,position,tool) DO UPDATE SET
base_ng=MAX(base_ng,excluded.base_ng),base_ok=MAX(base_ok,excluded.base_ok),
min_score=CASE WHEN min_score IS NULL THEN excluded.min_score WHEN excluded.min_score IS NULL THEN min_score ELSE MIN(min_score,excluded.min_score) END,
min_ng=CASE WHEN min_ng IS NULL THEN excluded.min_ng WHEN excluded.min_ng IS NULL THEN min_ng ELSE MIN(min_ng,excluded.min_ng) END,
max_ng=CASE WHEN max_ng IS NULL THEN excluded.max_ng WHEN excluded.max_ng IS NULL THEN max_ng ELSE MAX(max_ng,excluded.max_ng) END,
min_ok=CASE WHEN min_ok IS NULL THEN excluded.min_ok WHEN excluded.min_ok IS NULL THEN min_ok ELSE MIN(min_ok,excluded.min_ok) END;";
                    command.ExecuteNonQuery();
                    elapsed("tool-facts");
                    command.CommandText = RecalculateSql(true); command.ExecuteNonQuery(); elapsed("thresholds");
                    command.CommandText = CountersSql(1,true); command.ExecuteNonQuery(); elapsed("add-counters");
                    command.CommandText="INSERT OR IGNORE INTO workspace_refs SELECT i.position_key,COALESCE(i.workspace_name,''),COALESCE(i.workspace_key,'')"+rows+" GROUP BY 1,2,3;";command.ExecuteNonQuery();
                    cancellation.ThrowIfCancellationRequested();
                    command.CommandText = "UPDATE meta SET cursor=@next WHERE run_id=@run"; command.ExecuteNonQuery();
                    tx.Commit(); elapsed("commit"); return true;
                    }
                }
            }
        }

        private static string RebuildCountersSql()
        { return "DELETE FROM position_stats; DELETE FROM cell_stats; DELETE FROM tool_stats;DELETE FROM unknown_stats;"+CountersSql(1,false); }
        private static string CountersSql(int sign, bool affectedOnly)
        {
            string groupFilter=affectedOnly ? " WHERE (day,cell,position) IN (SELECT day,cell,position FROM affected)" : " WHERE 1";
            string cellFilter=affectedOnly ? " WHERE (day,cell) IN (SELECT day,cell FROM affected)" : " WHERE 1";
            string n=sign.ToString(CultureInfo.InvariantCulture);
            return "INSERT INTO position_stats SELECT day,position,"+n+"*COUNT(*),"+n+"*SUM(ng),"+n+"*SUM(rows),"+n+"*SUM(rows>1) FROM cells"+groupFilter+@" GROUP BY day,position
ON CONFLICT(day,position) DO UPDATE SET total=total+excluded.total,ng=ng+excluded.ng,raw_rows=raw_rows+excluded.raw_rows,duplicates=duplicates+excluded.duplicates;"+
"INSERT INTO tool_stats SELECT day,position,tool,"+n+"*COUNT(*),"+n+"*SUM(ng) FROM tools INDEXED BY sqlite_autoindex_tools_1"+groupFilter+@" GROUP BY day,position,tool
ON CONFLICT(day,position,tool) DO UPDATE SET total=total+excluded.total,ng=ng+excluded.ng;"+
"INSERT INTO cell_stats SELECT day,"+n+"*COUNT(*),"+n+"*SUM(ng) FROM (SELECT day,cell,MAX(ng) AS ng FROM cells"+cellFilter+@" GROUP BY day,cell) WHERE 1 GROUP BY day
ON CONFLICT(day) DO UPDATE SET total=total+excluded.total,ng=ng+excluded.ng;"+
"INSERT INTO unknown_stats SELECT day,position,"+n+@"*SUM(CASE WHEN base_ng=0 AND base_ok=0 AND NOT EXISTS(SELECT 1 FROM tools t WHERE t.day=c.day AND t.cell=c.cell AND t.position=c.position) THEN 1 ELSE 0 END) FROM cells c"+groupFilter+@" GROUP BY day,position
ON CONFLICT(day,position) DO UPDATE SET count=count+excluded.count;";
        }

        private static string RecalculateSql(bool changedOnly)
        {
            string toolWhere = changedOnly ? " WHERE (day,cell,position) IN (SELECT day,cell,position FROM affected)" : "";
            return @"UPDATE tools INDEXED BY sqlite_autoindex_tools_1 SET ng=CASE WHEN base_ng=1 AND (max_ng IS NULL OR max_ng>=COALESCE((SELECT value FROM thresholds h WHERE h.position=tools.position AND h.tool=tools.tool),0.5)) THEN 1 ELSE 0 END" + toolWhere + @";
UPDATE cells SET ng=CASE WHEN EXISTS(SELECT 1 FROM tools t WHERE t.day=cells.day AND t.cell=cells.cell AND t.position=cells.position)
THEN EXISTS(SELECT 1 FROM tools t WHERE t.day=cells.day AND t.cell=cells.cell AND t.position=cells.position AND t.ng=1) ELSE base_ng END" + toolWhere + ";";
        }

        internal void SetThresholds(IEnumerable<ThresholdValue> values, CancellationToken cancellation)
        {
            lock (_sync) using (var tx = _connection.BeginTransaction(System.Data.IsolationLevel.ReadCommitted)) using (var command = Command("DELETE FROM thresholds"))
            using(cancellation.Register(()=>command.Cancel()))
            {
                cancellation.ThrowIfCancellationRequested();
                command.Transaction = tx; command.ExecuteNonQuery();
                foreach (var value in values)
                {
                    if (double.IsNaN(value.value) || double.IsInfinity(value.value) || value.value < 0 || value.value > 1) throw new InvalidDataException("Invalid threshold");
                    command.CommandText = "INSERT INTO thresholds VALUES(@position,@tool,@value)";
                    command.Parameters.Clear(); command.Parameters.AddWithValue("@position", value.position);
                    command.Parameters.AddWithValue("@tool", value.tool); command.Parameters.AddWithValue("@value", value.value); command.ExecuteNonQuery();
                }
                cancellation.ThrowIfCancellationRequested(); command.CommandText = RecalculateSql(false)+RebuildCountersSql(); command.ExecuteNonQuery();
                cancellation.ThrowIfCancellationRequested(); tx.Commit();
            }
        }
        internal sealed class ThresholdValue { public string position; public string tool; public double value; }

        internal sealed class ActualNgValue { public string day,cell,position,path; public bool wildcard; }
        internal void StageActualNg(string batchId,string action,IEnumerable<ActualNgValue> values,CancellationToken cancellation)
        {
            Guid token;if(!Guid.TryParseExact(batchId,"N",out token))throw new InvalidDataException("Invalid actual-NG batch ID");
            if(action!="begin"&&action!="append"&&action!="commit")throw new InvalidDataException("Invalid actual-NG operation");
            lock(_sync)using(var tx=_connection.BeginTransaction(System.Data.IsolationLevel.ReadCommitted))using(var command=Command(""))
            using(cancellation.Register(()=>command.Cancel()))
            {
                command.Transaction=tx;cancellation.ThrowIfCancellationRequested();
                command.CommandText=@"CREATE TABLE IF NOT EXISTS actual_stage(day TEXT NOT NULL,cell TEXT NOT NULL,position TEXT NOT NULL,path TEXT NOT NULL,wildcard INTEGER NOT NULL,PRIMARY KEY(day,cell,position,path,wildcard)) WITHOUT ROWID;
CREATE TABLE IF NOT EXISTS actual_stage_token(id INTEGER PRIMARY KEY,token TEXT NOT NULL);";command.ExecuteNonQuery();
                if(action=="begin")
                {
                    command.CommandText="DELETE FROM actual_stage;DELETE FROM actual_stage_token;INSERT INTO actual_stage_token VALUES(1,@token);";command.Parameters.AddWithValue("@token",batchId);command.ExecuteNonQuery();
                }
                else
                {
                    command.CommandText="SELECT token FROM actual_stage_token WHERE id=1";
                    if(Convert.ToString(command.ExecuteScalar())!=batchId)throw new InvalidDataException("Actual-NG input was replaced. Retry the current input.");
                }
                if(action=="append")
                {
                    command.CommandText="INSERT OR IGNORE INTO actual_stage VALUES(@day,@cell,@position,@path,@wildcard)";command.Parameters.Clear();
                    foreach(string key in new[]{"@day","@cell","@position","@path","@wildcard"})command.Parameters.AddWithValue(key,"");
                    command.Prepare();int count=0;
                    foreach(var value in values??Enumerable.Empty<ActualNgValue>())
                    {
                        cancellation.ThrowIfCancellationRequested();if(++count>1000)throw new InvalidDataException("Actual-NG batches must contain at most 1000 rows");
                        if(value==null||string.IsNullOrWhiteSpace(value.cell)||string.IsNullOrWhiteSpace(value.position))throw new InvalidDataException("Actual NG Cell/Position is empty");
                        command.Parameters["@day"].Value=value.day??"";command.Parameters["@cell"].Value=value.cell;command.Parameters["@position"].Value=value.position;command.Parameters["@path"].Value=value.path??"";command.Parameters["@wildcard"].Value=value.wildcard?1:0;command.ExecuteNonQuery();
                    }
                }
                if(action=="commit") {command.CommandText="DELETE FROM actual_ng;INSERT INTO actual_ng SELECT * FROM actual_stage;DELETE FROM actual_stage;DELETE FROM actual_stage_token;";command.ExecuteNonQuery();}
                cancellation.ThrowIfCancellationRequested();tx.Commit();
            }
        }
        internal void ReplaceActualNg(IEnumerable<ActualNgValue> values, CancellationToken cancellation)
        {
            lock(_sync) using(var tx=_connection.BeginTransaction(System.Data.IsolationLevel.ReadCommitted))
            using(var command=Command("DELETE FROM actual_ng"))
            {
                command.Transaction=tx;command.ExecuteNonQuery();
                command.CommandText="INSERT OR IGNORE INTO actual_ng VALUES(@day,@cell,@position,@path,@wildcard)";
                command.Parameters.Clear();
                foreach(string key in new[]{"@day","@cell","@position","@path","@wildcard"}) command.Parameters.AddWithValue(key,"");
                command.Prepare();
                foreach(var value in values)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if(string.IsNullOrWhiteSpace(value.cell) || string.IsNullOrWhiteSpace(value.position)) throw new InvalidDataException("Actual NG Cell/Position is empty");
                    command.Parameters["@day"].Value=value.day ?? "";command.Parameters["@cell"].Value=value.cell;
                    command.Parameters["@position"].Value=value.position;command.Parameters["@path"].Value=value.path ?? "";
                    command.Parameters["@wildcard"].Value=value.wildcard ? 1 : 0;command.ExecuteNonQuery();
                }
                cancellation.ThrowIfCancellationRequested();tx.Commit();
            }
        }

        // Raw observations remain individually selectable even when the dashboard deduplicates a Cell.
        // Keyset pagination preserves equal-score order and avoids growing OFFSET scans.
        internal object ScorePage(string tool,string position,string scope,double afterScore,long afterId,int requestedSize)
        {
            if(scope!="TOOL_OK" && scope!="TOOL_NG" && scope!="ACTUAL_NG_TOOL_NG" && scope!="ACTUAL_NG_TOOL_OK") throw new InvalidDataException("Invalid score scope");
            if(double.IsNaN(afterScore)||double.IsInfinity(afterScore)||afterScore < -1 || afterScore > 1) throw new InvalidDataException("Invalid score cursor");
            int size=Math.Max(1,Math.Min(500,requestedSize));
            lock(_sync) using(var command=Command(@"SELECT t.tool_result_id AS observationId,i.image_id AS imageId,i.cell_id AS cellId,i.position_key AS position,
i.capture_timestamp AS captureTimestamp,i.full_path AS fullPath,i.processed_path AS processedPath,i.source_file_name AS sourceFileName,i.source_row_number AS sourceRowNumber,
i.workspace_key AS workspaceKey,i.workspace_name AS workspaceName,t.result,t.score,t.overlay_path AS overlayPath
FROM source.tool_results t JOIN source.images i ON i.image_id=t.image_id
JOIN cells c ON c.day=substr(COALESCE(i.capture_timestamp,''),1,10) AND c.cell=i.cell_id AND c.position=i.position_key
WHERE t.run_id=@run AND t.tool_name=@tool AND t.score IS NOT NULL AND (@position='' OR i.position_key=@position)
AND ((@scope='TOOL_OK' AND t.result='OK') OR (@scope IN ('TOOL_NG','ACTUAL_NG_TOOL_NG') AND t.result='NG') OR (@scope='ACTUAL_NG_TOOL_OK' AND "+FinalOk+@"))
AND (@scope IN ('TOOL_OK','TOOL_NG') OR EXISTS(SELECT 1 FROM actual_ng a WHERE a.cell=c.cell AND a.position=c.position AND (a.day=c.day OR a.wildcard=1)))
AND (t.score>@score OR (t.score=@score AND t.tool_result_id>@id))
ORDER BY t.score,t.tool_result_id LIMIT @limit"))
            {
                command.Parameters.AddWithValue("@tool",tool ?? "");command.Parameters.AddWithValue("@position",position=="ALL" ? "" : position ?? "");
                command.Parameters.AddWithValue("@scope",scope);command.Parameters.AddWithValue("@score",afterScore);command.Parameters.AddWithValue("@id",afterId);command.Parameters.AddWithValue("@limit",size+1);
                var records=new List<Dictionary<string,object>>();
                using(var reader=command.ExecuteReader()) while(reader.Read())
                {
                    var row=new Dictionary<string,object>();for(int i=0;i<reader.FieldCount;i++)row.Add(reader.GetName(i),reader.IsDBNull(i)?null:reader.GetValue(i));records.Add(row);
                }
                bool more=records.Count>size;if(more)records.RemoveAt(records.Count-1);
                var last=records.Count==0?null:records[records.Count-1];
                return new {records,hasMore=more,nextScore=last==null?afterScore:Convert.ToDouble(last["score"]),nextId=last==null?afterId:Convert.ToInt64(last["observationId"])};
            }
        }

        // Only small aggregates leave SQLite. Date lists are paged independently of row counts.
        internal Dictionary<string, object> Summary(string date = "", int dateOffset = 0)
        {
            lock (_sync)
            {
                const string filter = " WHERE (@date='' OR day=@date)";
                return new Dictionary<string, object> {
                    {"cursor",Cursor},
                    {"totals",Read("SELECT COALESCE(SUM(total),0) AS total,COALESCE(SUM(ng),0) AS ng,COALESCE(SUM(raw_rows),0) AS rawRows,COALESCE(SUM(duplicates),0) AS duplicateGroups FROM position_stats"+filter,date)},
                    {"cells",Read("SELECT COALESCE(SUM(total),0) AS total,COALESCE(SUM(ng),0) AS ng FROM cell_stats"+filter,date)},
                    {"positions",Read("SELECT position,SUM(total) AS total,SUM(ng) AS ng,SUM(raw_rows) AS rawRows FROM position_stats"+filter+" GROUP BY position",date)},
                    {"tools",Read("SELECT position,tool,SUM(ng) AS ng,SUM(total) AS total FROM tool_stats"+filter+" GROUP BY position,tool",date)},
                    {"dateCount",Read("SELECT COUNT(DISTINCT day) AS total FROM position_stats WHERE day<>''",date)},
                    {"dates",Read("SELECT day AS date,total,ng FROM cell_stats WHERE day<>'' ORDER BY day DESC LIMIT 10 OFFSET @offset",date,Math.Max(0,dateOffset))}
                };
            }
        }
        private List<Dictionary<string, object>> Read(string sql, string date, int offset = 0)
        {
            var result = new List<Dictionary<string, object>>();
            using (var command = Command(sql))
            {
                command.Parameters.AddWithValue("@date", date ?? ""); command.Parameters.AddWithValue("@offset", offset);
                using (var reader = command.ExecuteReader()) while (reader.Read())
                {
                    var row = new Dictionary<string, object>();
                    for (int i = 0; i < reader.FieldCount; i++) row.Add(reader.GetName(i), reader.IsDBNull(i) ? null : reader.GetValue(i));
                    result.Add(row);
                }
            }
            return result;
        }
        // Date-aware actual-NG keys. CSV-only IDs expand to every matching capture day;
        // a wildcard with no match remains one unmatched key, as in browser analysis.
        private const string ActualKeys = @"WITH actual_keys AS (
SELECT day,cell,position FROM actual_ng WHERE wildcard=0
UNION SELECT c.day,c.cell,c.position FROM actual_ng a CROSS JOIN cells c INDEXED BY cells_actual WHERE a.wildcard=1 AND a.cell=c.cell AND a.position=c.position
UNION SELECT '',a.cell,a.position FROM actual_ng a WHERE a.wildcard=1 AND NOT EXISTS(SELECT 1 FROM cells c WHERE c.cell=a.cell AND c.position=a.position)
) ";
        private const string FinalOk = "(c.ng=0 AND (c.base_ok=1 OR c.base_ng=1 OR EXISTS(SELECT 1 FROM tools t WHERE t.day=c.day AND t.cell=c.cell AND t.position=c.position)))";

        private List<Dictionary<string,object>> Query(string sql,CancellationToken cancellation,params object[] parameters)
        {
            cancellation.ThrowIfCancellationRequested();
            using(var command=Command(sql)) using(cancellation.Register(()=>command.Cancel()))
            {
                for(int i=0;i<parameters.Length;i+=2)command.Parameters.AddWithValue((string)parameters[i],parameters[i+1]??DBNull.Value);
                var rows=new List<Dictionary<string,object>>();
                using(var reader=command.ExecuteReader())while(reader.Read())
                {
                    cancellation.ThrowIfCancellationRequested();var row=new Dictionary<string,object>();
                    for(int i=0;i<reader.FieldCount;i++)row[reader.GetName(i)]=reader.IsDBNull(i)?null:reader.GetValue(i);
                    rows.Add(row);
                }
                return rows;
            }
        }

        internal object ActualSummary(string date,CancellationToken cancellation)
        {
            lock(_sync) return Query(ActualKeys+@"SELECT a.position,COUNT(*) AS actualNg,
SUM(CASE WHEN c.cell IS NOT NULL THEN 1 ELSE 0 END) AS matched,
SUM(CASE WHEN c.cell IS NULL THEN 1 ELSE 0 END) AS unmatched,
SUM(CASE WHEN c.ng=1 THEN 1 ELSE 0 END) AS detected,
SUM(CASE WHEN "+FinalOk+@" THEN 1 ELSE 0 END) AS misses
FROM actual_keys a LEFT JOIN cells c ON c.day=a.day AND c.cell=a.cell AND c.position=a.position
WHERE @date='' OR a.day=@date GROUP BY a.position",cancellation,"@date",date??"");
        }
        private string PreparePositions(string[] positions,string column)
        {
            if(positions==null)return "1=1";
            if(positions.Length>1000)throw new InvalidDataException("Position selection exceeds 1000");
            Execute("CREATE TEMP TABLE IF NOT EXISTS selected_positions(position TEXT PRIMARY KEY); DELETE FROM selected_positions;");
            using(var command=Command("INSERT OR IGNORE INTO selected_positions VALUES(@position)"))
            {var parameter=command.Parameters.Add("@position",System.Data.DbType.String);foreach(var position in positions){parameter.Value=position??"";command.ExecuteNonQuery();}}
            return column+" IN (SELECT position FROM selected_positions)";
        }

        internal object DateWindow(int offset,string anchor,CancellationToken cancellation,string[] positions=null,string selectedDate="",bool allDates=false)
        {
            lock(_sync)
            {
                string where=PreparePositions(positions,"position");
                var range=Query("SELECT COUNT(DISTINCT NULLIF(day,'')) AS count,MIN(NULLIF(day,'')) AS firstDate,MAX(NULLIF(day,'')) AS lastDate FROM position_stats WHERE "+where,cancellation)[0];
                long count=Number(range,"count"),start=offset<0?Math.Max(0,count-10):offset;
                if(!string.IsNullOrEmpty(anchor))start=Number(Query("SELECT COUNT(DISTINCT day) AS count FROM position_stats WHERE "+where+" AND day<>'' AND day<@date",cancellation,"@date",anchor)[0],"count");
                start=Math.Max(0,Math.Min(Math.Max(0,count-10),start));
                // All Positions use existing incremental Cell counters; selected Positions are merged by day + Cell.
                string cellSource=positions==null ? "SELECT day,total,ng FROM cell_stats" : "SELECT day,COUNT(*) AS total,SUM(ng) AS ng FROM (SELECT day,cell,MAX(ng) AS ng FROM cells WHERE "+where+" GROUP BY day,cell) GROUP BY day";
                if(allDates)start=0;
                var rows=Query("SELECT day AS date,total,ng FROM ("+cellSource+") WHERE day<>'' ORDER BY day"+(allDates?"":" LIMIT 10 OFFSET @offset"),cancellation,"@offset",start);
                foreach(var row in rows)row["ngRate"]=Number(row,"total")==0?0:(double)Number(row,"ng")/Number(row,"total");
                var summary=Query("SELECT COALESCE(SUM(total),0) AS totalCount,COALESCE(SUM(ng),0) AS ngCount FROM ("+cellSource+") WHERE @date='' OR day=@date",cancellation,"@date",selectedDate??"")[0];
                summary["unknown"]=Query("SELECT COALESCE(SUM(raw_rows),0) AS count FROM position_stats WHERE "+where+" AND day='' AND (@date='' OR day=@date)",cancellation,"@date",selectedDate??"")[0]["count"];
                return new {rows,start,count,firstDate=range["firstDate"],lastDate=range["lastDate"],summary};
            }
        }

        internal object Dashboard(string date,double exclusion,CancellationToken cancellation)
        {
            if(double.IsNaN(exclusion)||double.IsInfinity(exclusion)||exclusion<0||exclusion>1)throw new InvalidDataException("Invalid exclusion threshold");
            lock(_sync)
            {
                var summary=Summary(date);
                var totals=((List<Dictionary<string,object>>)summary["totals"])[0];
                var cells=((List<Dictionary<string,object>>)summary["cells"])[0];
                var positions=(List<Dictionary<string,object>>)summary["positions"];
                var actual=(List<Dictionary<string,object>>)ActualSummary(date,cancellation);
                var unknown=Query("SELECT position,SUM(count) AS count FROM unknown_stats WHERE @date='' OR day=@date GROUP BY position",cancellation,"@date",date??"");
                var tools=Query(@"SELECT s.position,s.tool,SUM(s.ng) AS ng,COALESCE(h.value,.5) AS threshold
FROM tool_stats s LEFT JOIN thresholds h ON h.position=s.position AND h.tool=s.tool
WHERE @date='' OR s.day=@date GROUP BY s.position,s.tool ORDER BY s.position,s.tool",cancellation,"@date",date??"");
                // Minima use original NG scores; another tool's original NG score excludes the whole Cell.
                var minima=actual.Count==0?new List<Dictionary<string,object>>():Query(ActualKeys+@"SELECT t.position,t.tool,MIN(t.min_ng) AS minNgScore FROM actual_keys a
CROSS JOIN tools t INDEXED BY sqlite_autoindex_tools_1 ON t.day=a.day AND t.cell=a.cell AND t.position=a.position
WHERE (@date='' OR t.day=@date) AND t.min_ng IS NOT NULL AND NOT EXISTS(
SELECT 1 FROM tools other WHERE other.day=t.day AND other.cell=t.cell AND other.position=t.position
AND lower(other.tool)<>lower(t.tool) AND other.max_ng>=@exclusion) GROUP BY t.position,t.tool",cancellation,"@date",date??"","@exclusion",exclusion);
                var names=positions.Select(p=>Convert.ToString(p["position"])).Union(actual.Select(p=>Convert.ToString(p["position"]))).Distinct().OrderBy(p=>p).ToArray();
                var positionSummaries=new List<object>();var positionTools=new List<object>();
                foreach(string name in names)
                {
                    var p=positions.FirstOrDefault(row=>Convert.ToString(row["position"])==name);
                    var a=actual.FirstOrDefault(row=>Convert.ToString(row["position"])==name);
                    long total=Number(p,"total"),ng=Number(p,"ng"),unrecognized=Number(unknown.FirstOrDefault(row=>Convert.ToString(row["position"])==name),"count");
                    positionSummaries.Add(new {position=name,input=p!=null,total,rawRows=Number(p,"rawRows"),ng,ok=total-ng-unrecognized,ngRate=total==0?0:(double)ng/total,actualNg=Number(a,"actualNg"),detected=Number(a,"detected"),misses=Number(a,"misses"),unmatched=Number(a,"unmatched")});
                    positionTools.Add(new {position=name,input=p!=null,totalNg=ng,tools=tools.Where(t=>Convert.ToString(t["position"])==name).Select(t=>{
                        var minimum=minima.FirstOrDefault(m=>Convert.ToString(m["position"])==name&&Convert.ToString(m["tool"])==Convert.ToString(t["tool"]));
                        return new {tool=t["tool"],ng=Number(t,"ng"),denominator=ng,rate=ng==0?0:(double)Number(t,"ng")/ng,minNgScore=minimum==null?null:minimum["minNgScore"],threshold=t["threshold"],actualNgExclusionThreshold=exclusion};
                    }).ToArray()});
                }
                var dates=Query("SELECT day AS date,total,ng FROM cell_stats WHERE day<>'' ORDER BY day DESC LIMIT 10",cancellation);
                dates.Reverse();foreach(var row in dates)row["ngRate"]=Number(row,"total")==0?0:(double)Number(row,"ng")/Number(row,"total");
                var range=Query("SELECT MIN(NULLIF(day,'')) AS firstDate,MAX(NULLIF(day,'')) AS lastDate,COUNT(DISTINCT NULLIF(day,'')) AS dateCount FROM position_stats",cancellation)[0];
                var workspaces=Query("SELECT position,workspace_name AS workspaceName,workspace_key AS workspaceKey FROM workspace_refs ORDER BY position,workspace_name LIMIT 10001",cancellation);
                if(workspaces.Count>10000)throw new InvalidDataException("워크스페이스 종류가 10,000개를 초과합니다. 입력 메타데이터를 확인하세요.");
                long cellCount=Number(cells,"total"),ngCellCount=Number(cells,"ng");
                return new {remote=true,recordCount=Number(totals,"total"),rawRowCount=Number(totals,"rawRows"),uniqueCellCount=cellCount,ngCellCount,ngCellRate=cellCount==0?0:(double)ngCellCount/cellCount,
                    missCount=actual.Sum(a=>Number(a,"misses")),actualUniqueCount=actual.Sum(a=>Number(a,"actualNg")),matchedActualCount=actual.Sum(a=>Number(a,"matched")),unmatchedActualCount=actual.Sum(a=>Number(a,"unmatched")),
                    duplicateCount=Number(totals,"duplicateGroups"),positionSummaries,positionToolSummaries=positionTools,tools=tools.Select(t=>Convert.ToString(t["tool"])).Distinct().OrderBy(t=>t).ToArray(),
                    daily=dates,dailyUnit="cell",dateRange=range,workspaces,totalNg=Number(totals,"ng"),cursor=Cursor,
                    unknownRows=Query("SELECT COALESCE(SUM(raw_rows),0) AS count FROM position_stats WHERE day=''",cancellation)[0]["count"]};
            }
        }
        private static long Number(Dictionary<string,object> row,string key)
        { return row==null||!row.ContainsKey(key)||row[key]==null?0:Convert.ToInt64(row[key]); }

        internal object ExportRaw(string outputDirectory,long maxRows,bool splitByDate,CancellationToken cancellation,Action<long> progress)
        {
            string root=Path.GetFullPath(outputDirectory);if(!Directory.Exists(root))throw new DirectoryNotFoundException(root);
            string token=Guid.NewGuid().ToString("N"),stage=Path.Combine(root,".VisionQC-export-"+token);
            Directory.CreateDirectory(stage);var published=new List<string>();long count=0;
            try
            {
                lock(_sync)
                {
                    var tools=Query("SELECT DISTINCT tool_name AS tool FROM source.tool_results WHERE run_id=@run ORDER BY tool_name",cancellation).Select(r=>Convert.ToString(r["tool"])).ToArray();
                    var header=new List<string>{"Date","Time","Cell ID","Position","Total_result","FullPath","ProcessedPath","Source_File","Source_Row","WorkspaceType","WorkspaceName","WorkspaceKey"};
                    foreach(var tool in tools){header.Add(tool+"_result");header.Add(tool+"_score");}
                    var indexes=tools.Select((name,index)=>new{name,index}).ToDictionary(x=>x.name,x=>12+x.index*2,StringComparer.Ordinal);
                    using(var writer=new PartitionedCsvWriter(Path.Combine(stage,"VisionQC_results_"+DateTime.Now.ToString("yyyyMMdd_HHmmss")+"_"+token.Substring(0,8)+".csv"),maxRows,splitByDate))
                    using(var command=Command(@"SELECT i.image_id,i.capture_timestamp,i.cell_id,i.position_key,i.total_result,i.full_path,i.processed_path,i.source_file_name,i.source_row_number,
i.workspace_type,i.workspace_name,i.workspace_key,t.tool_name,t.result,t.score
FROM source.images i LEFT JOIN source.tool_results t ON t.image_id=i.image_id
WHERE i.run_id=@run ORDER BY i.image_id,t.tool_result_id"))
                    using(cancellation.Register(()=>command.Cancel()))
                    using(var reader=command.ExecuteReader())
                    {
                        writer.WriteLine(ResultCsv.WriteRecord(header));long previous=-1;string[] values=null;
                        Action flush=()=>{if(values==null)return;writer.WriteLine(ResultCsv.WriteRecord(values));count++;if(count%1000==0)progress?.Invoke(count);};
                        while(reader.Read())
                        {
                            cancellation.ThrowIfCancellationRequested();long imageId=reader.GetInt64(0);
                            if(imageId!=previous)
                            {
                                flush();values=new string[header.Count];var capture=ResultCsv.SplitCaptureTimestamp(Convert.ToString(reader[1],CultureInfo.InvariantCulture));
                                values[0]=capture[0];values[1]=capture[1];
                                for(int i=2;i<12;i++)values[i]=reader.IsDBNull(i)?"":Convert.ToString(reader[i],CultureInfo.InvariantCulture);
                                previous=imageId;
                            }
                            if(!reader.IsDBNull(12))
                            {
                                int index=indexes[reader.GetString(12)];values[index]=reader.IsDBNull(13)?"":reader.GetString(13);
                                values[index+1]=reader.IsDBNull(14)?"":reader.GetDouble(14).ToString("R",CultureInfo.InvariantCulture);
                            }
                        }
                        flush();cancellation.ThrowIfCancellationRequested();
                    }
                }
                cancellation.ThrowIfCancellationRequested();
                foreach(var path in Directory.GetFiles(stage))
                {cancellation.ThrowIfCancellationRequested();var target=Path.Combine(root,Path.GetFileName(path));File.Move(path,target);published.Add(target);}
                progress?.Invoke(count);return new {count,files=published.Where(p=>p.EndsWith(".csv",StringComparison.OrdinalIgnoreCase)).ToArray()};
            }
            catch {foreach(var file in published)File.Delete(file);throw;}
            finally {Directory.Delete(stage,true);}
        }

        internal object ExportSelection(string outputDirectory,long maxRows,bool splitByDate,string date,string[] positions,string tool,CancellationToken cancellation)
        {
            string root=Path.GetFullPath(outputDirectory);if(!Directory.Exists(root))throw new DirectoryNotFoundException(root);
            string token=Guid.NewGuid().ToString("N"),stage=Path.Combine(root,".VisionQC-export-"+token);Directory.CreateDirectory(stage);
            var published=new List<string>();long count=0;
            try
            {
                lock(_sync)
                {
                    string where=PreparePositions(positions,"c.position");
                    var names=Query("SELECT DISTINCT tool FROM tools ORDER BY tool",cancellation).Select(r=>Convert.ToString(r["tool"])).ToArray();
                    var header=new List<string>{"Date","Time","Cell ID","Position","Total_result","Source_Row_Count"};
                    foreach(string name in names)header.AddRange(new[]{name+"_result",name+"_score",name+"_threshold"});
                    var indexes=names.Select((name,index)=>new{name,index}).ToDictionary(t=>t.name,t=>6+t.index*3);
                    using(var writer=new PartitionedCsvWriter(Path.Combine(stage,"VisionQC_"+(string.IsNullOrEmpty(tool)?"selection":"tool_NG")+"_"+token.Substring(0,8)+".csv"),maxRows,splitByDate))
                    using(var command=Command(@"SELECT c.day,c.cell,c.position,c.ng,c.rows,
CASE WHEN "+FinalOk+@" THEN 1 ELSE 0 END AS is_ok,t.tool,t.base_ng,t.base_ok,t.ng,t.min_ng,t.min_ok,t.min_score,COALESCE(h.value,.5)
FROM cells c LEFT JOIN tools t ON t.day=c.day AND t.cell=c.cell AND t.position=c.position
LEFT JOIN thresholds h ON h.position=t.position AND h.tool=t.tool
WHERE "+where+@" AND (@date='' OR c.day=@date) AND (@tool='' OR EXISTS(SELECT 1 FROM tools n WHERE n.day=c.day AND n.cell=c.cell AND n.position=c.position AND n.tool=@tool AND n.ng=1))
ORDER BY c.day,c.position,c.cell,t.tool"))
                    using(cancellation.Register(()=>command.Cancel()))
                    {
                        command.Parameters.AddWithValue("@date",date??"");command.Parameters.AddWithValue("@tool",tool??"");writer.WriteLine(ResultCsv.WriteRecord(header));
                        using(var reader=command.ExecuteReader())
                        {
                            string lastDay=null,lastCell=null,lastPosition=null;string[] values=null;
                            Action flush=()=>{if(values!=null){writer.WriteLine(ResultCsv.WriteRecord(values));count++;}};
                            while(reader.Read())
                            {
                                cancellation.ThrowIfCancellationRequested();string day=reader.GetString(0),cell=reader.GetString(1),position=reader.GetString(2);
                                if(day!=lastDay||cell!=lastCell||position!=lastPosition){flush();values=new string[header.Count];values[0]=day;values[1]="";values[2]=cell;values[3]=position;values[4]=reader.GetInt64(3)==1?"NG":reader.GetInt64(5)==1?"OK":"UNKNOWN";values[5]=Convert.ToString(reader[4]);lastDay=day;lastCell=cell;lastPosition=position;}
                                if(!reader.IsDBNull(6)){
                                    int index=indexes[reader.GetString(6)];values[index]=reader.GetInt64(9)==1?"NG":reader.GetInt64(7)==1||reader.GetInt64(8)==1?"OK":"UNKNOWN";
                                    int score=reader.GetInt64(7)==1?10:reader.GetInt64(8)==1?11:12;values[index+1]=reader.IsDBNull(score)?"":reader.GetDouble(score).ToString("R",CultureInfo.InvariantCulture);values[index+2]=reader.GetDouble(13).ToString("R",CultureInfo.InvariantCulture);
                                }
                            }flush();
                        }
                    }
                }
                foreach(string file in Directory.GetFiles(stage)){cancellation.ThrowIfCancellationRequested();string target=Path.Combine(root,Path.GetFileName(file));File.Move(file,target);published.Add(target);}
                return new {count,files=published.Where(p=>p.EndsWith(".csv",StringComparison.OrdinalIgnoreCase)).ToArray()};
            }
            catch{foreach(string file in published)File.Delete(file);throw;}
            finally{Directory.Delete(stage,true);}
        }

        internal object ExportFiltered(string outputDirectory,long maxRows,bool splitByDate,string kind,string date,string position,string tool,string scope,bool exclude,double exclusion,string compare,double cutoff,CancellationToken cancellation)
        {
            compare=(compare??"").ToUpperInvariant();
            if(double.IsNaN(exclusion)||double.IsInfinity(exclusion)||exclusion<0||exclusion>1)throw new InvalidDataException("Invalid exclusion threshold");
            if(kind!="misses"&&kind!="score")throw new InvalidDataException("Unknown CSV export type");
            if(kind=="score"&&(!new[]{"TOOL_OK","TOOL_NG","ACTUAL_NG_TOOL_NG","ACTUAL_NG_TOOL_OK"}.Contains(scope)||!new[]{"GTE","LTE"}.Contains(compare)||double.IsNaN(cutoff)||double.IsInfinity(cutoff)||cutoff<0||cutoff>1))throw new InvalidDataException("Invalid score export filter");
            string root=Path.GetFullPath(outputDirectory);if(!Directory.Exists(root))throw new DirectoryNotFoundException(root);
            string token=Guid.NewGuid().ToString("N"),stage=Path.Combine(root,".VisionQC-export-"+token);Directory.CreateDirectory(stage);
            var published=new List<string>();long count=0;
            try
            {
                lock(_sync)using(var writer=new PartitionedCsvWriter(Path.Combine(stage,"VisionQC_"+kind+"_"+DateTime.Now.ToString("yyyyMMdd_HHmmss")+"_"+token.Substring(0,8)+".csv"),maxRows,splitByDate))
                {
                    if(kind=="score")
                    {
                        writer.WriteLine("Date,Time,Cell ID,Position,Tool,Result,Score,Condition,FullPath,Source_File,Source_Row");
                        using(var command=Command("SELECT i.capture_timestamp,i.cell_id,i.position_key,t.tool_name,t.result,t.score,i.full_path,i.source_file_name,i.source_row_number"+ScoreSource(scope)+" AND t.score"+(compare=="GTE"?">=":"<=")+"@cutoff ORDER BY t.score,t.tool_result_id"))
                        using(cancellation.Register(()=>command.Cancel()))
                        {
                            command.Parameters.AddWithValue("@tool",tool??"");command.Parameters.AddWithValue("@position",position=="ALL"?"":position??"");command.Parameters.AddWithValue("@scope",scope);
                            command.Parameters.AddWithValue("@exclude",exclude?1:0);command.Parameters.AddWithValue("@exclusion",exclusion);command.Parameters.AddWithValue("@cutoff",cutoff);
                            using(var reader=command.ExecuteReader())while(reader.Read())
                            {
                                cancellation.ThrowIfCancellationRequested();var capture=ResultCsv.SplitCaptureTimestamp(Convert.ToString(reader[0]));
                                writer.WriteLine(ResultCsv.WriteRecord(new[]{capture[0],capture[1],Convert.ToString(reader[1]),Convert.ToString(reader[2]),Convert.ToString(reader[3]),Convert.ToString(reader[4]),reader.GetDouble(5).ToString("R",CultureInfo.InvariantCulture),"Score "+(compare=="GTE"?">= ":"<= ")+cutoff.ToString("R",CultureInfo.InvariantCulture),Convert.ToString(reader[6]),Convert.ToString(reader[7]),Convert.ToString(reader[8])}));count++;
                            }
                        }
                    }
                    else
                    {
                        var toolNames=Query("SELECT DISTINCT tool FROM tools ORDER BY tool",cancellation).Select(t=>Convert.ToString(t["tool"])).ToArray();
                        var header=new List<string>{"Date","Time","Cell ID","Position","Result"};foreach(var name in toolNames){header.Add(name+"_result");header.Add(name+"_score");header.Add(name+"_threshold");}
                        header.AddRange(new[]{"Actual_Image_Count","Actual_Image_Paths","Capture_Date","Source_Row_Count","FullPath"});writer.WriteLine(ResultCsv.WriteRecord(header));
                        using(var command=Command(ActualKeys+"SELECT c.day,c.cell,c.position,c.rows FROM actual_keys a JOIN cells c ON c.day=a.day AND c.cell=a.cell AND c.position=a.position WHERE "+FinalOk+" AND (@date='' OR c.day=@date) AND (@position='' OR c.position=@position) ORDER BY c.day,c.position,c.cell"))
                        using(cancellation.Register(()=>command.Cancel()))
                        {
                            command.Parameters.AddWithValue("@date",date??"");command.Parameters.AddWithValue("@position",position??"");
                            using(var reader=command.ExecuteReader())while(reader.Read())
                            {
                                cancellation.ThrowIfCancellationRequested();string day=reader.GetString(0),cell=reader.GetString(1),pos=reader.GetString(2);
                                object[] keys={"@day",day,"@cell",cell,"@position",pos};
                                var results=Query(@"SELECT t.tool,CASE WHEN t.ng=1 THEN 'NG' WHEN t.base_ng=1 OR t.base_ok=1 THEN 'OK' ELSE 'UNKNOWN' END AS result,
CASE WHEN t.base_ng=1 THEN t.min_ng WHEN t.base_ok=1 THEN t.min_ok ELSE t.min_score END AS score,COALESCE(h.value,.5) AS threshold
FROM tools t LEFT JOIN thresholds h ON h.position=t.position AND h.tool=t.tool WHERE t.day=@day AND t.cell=@cell AND t.position=@position",cancellation,keys).ToDictionary(t=>Convert.ToString(t["tool"]));
                                var source=Query(@"SELECT MIN(capture_timestamp) AS timestamp,group_concat(full_path,' | ') AS paths FROM source.images WHERE run_id=@run AND cell_id=@cell AND position_key=@position AND substr(COALESCE(capture_timestamp,''),1,10)=@day",cancellation,keys)[0];
                                var actual=Query("SELECT COUNT(*) AS count,group_concat(path,' | ') AS paths FROM actual_ng WHERE cell=@cell AND position=@position AND (day=@day OR wildcard=1)",cancellation,keys)[0];
                                var capture=ResultCsv.SplitCaptureTimestamp(Convert.ToString(source["timestamp"]));var values=new List<string>{day,capture[1],cell,pos,"OK"};
                                foreach(var name in toolNames)
                                {
                                    Dictionary<string,object> result;results.TryGetValue(name,out result);values.Add(result==null?"":Convert.ToString(result["result"]));
                                    values.Add(result==null||result["score"]==null?"":Convert.ToDouble(result["score"]).ToString("R",CultureInfo.InvariantCulture));values.Add(result==null?"":Convert.ToDouble(result["threshold"]).ToString("R",CultureInfo.InvariantCulture));
                                }
                                values.AddRange(new[]{Convert.ToString(actual["count"]),Convert.ToString(actual["paths"]),day,Convert.ToString(reader[3]),Convert.ToString(source["paths"])});writer.WriteLine(ResultCsv.WriteRecord(values));count++;
                            }
                        }
                    }
                }
                cancellation.ThrowIfCancellationRequested();foreach(var file in Directory.GetFiles(stage)){cancellation.ThrowIfCancellationRequested();var target=Path.Combine(root,Path.GetFileName(file));File.Move(file,target);published.Add(target);}
                return new {count,files=published.Where(p=>p.EndsWith(".csv",StringComparison.OrdinalIgnoreCase)).ToArray()};
            }
            catch{foreach(var path in published)File.Delete(path);throw;}
            finally{Directory.Delete(stage,true);}
        }

        private const string ScoreFrom = @" FROM source.tool_results t JOIN source.images i ON i.image_id=t.image_id
JOIN cells c ON c.day=substr(COALESCE(i.capture_timestamp,''),1,10) AND c.cell=i.cell_id AND c.position=i.position_key
WHERE t.run_id=@run AND i.image_id<=(SELECT cursor FROM meta WHERE run_id=@run) AND t.tool_name=@tool AND t.score IS NOT NULL AND (@position='' OR i.position_key=@position)
AND ((@scope='TOOL_OK' AND t.result='OK') OR (@scope IN ('TOOL_NG','ACTUAL_NG_TOOL_NG') AND t.result='NG') OR (@scope='ACTUAL_NG_TOOL_OK' AND "+FinalOk+@"))
AND (@scope IN ('TOOL_OK','TOOL_NG') OR EXISTS(SELECT 1 FROM actual_ng a WHERE a.cell=c.cell AND a.position=c.position AND (a.day=c.day OR a.wildcard=1)))
AND (@exclude=0 OR @scope<>'ACTUAL_NG_TOOL_NG' OR NOT EXISTS(SELECT 1 FROM tools other WHERE other.day=c.day AND other.cell=c.cell AND other.position=c.position AND lower(other.tool)<>lower(t.tool_name) AND other.max_ng>=@exclusion)) ";

        private static string ScoreSource(string scope)
        {
            if(scope!="TOOL_OK"&&scope!="TOOL_NG")return ScoreFrom.Replace(
                " FROM source.tool_results t JOIN source.images i ON i.image_id=t.image_id",
                " FROM ("+ActualKeys+"SELECT day,cell,position FROM actual_keys) a CROSS JOIN source.images i INDEXED BY idx_images_cell_capture ON i.cell_id=a.cell AND i.position_key=a.position AND substr(COALESCE(i.capture_timestamp,''),1,10)=a.day CROSS JOIN source.tool_results t INDEXED BY idx_tool_results_image ON t.image_id=i.image_id");
            return @" FROM source.tool_results t JOIN source.images i ON i.image_id=t.image_id
WHERE t.run_id=@run AND i.image_id<=(SELECT cursor FROM meta WHERE run_id=@run) AND t.tool_name=@tool AND t.score IS NOT NULL
AND (@position='' OR i.position_key=@position) AND t.result=CASE @scope WHEN 'TOOL_OK' THEN 'OK' ELSE 'NG' END ";
        }

        internal object ScoreStatistics(string tool,string position,string scope,bool exclude,double exclusion,CancellationToken cancellation)
        {
            if(!new[]{"TOOL_OK","TOOL_NG","ACTUAL_NG_TOOL_NG","ACTUAL_NG_TOOL_OK"}.Contains(scope))throw new InvalidDataException("Invalid score scope");
            if(double.IsNaN(exclusion)||double.IsInfinity(exclusion)||exclusion<0||exclusion>1)throw new InvalidDataException("Invalid exclusion threshold");
            object[] args={"@tool",tool??"","@position",position=="ALL"?"":position??"","@scope",scope,"@exclude",exclude?1:0,"@exclusion",exclusion};
            lock(_sync)
            {
                var total=Query("SELECT COUNT(*) AS count,MIN(t.score) AS min,MAX(t.score) AS max,AVG(t.score) AS mean"+ScoreSource(scope),cancellation,args)[0];
                long count=Convert.ToInt64(total["count"]);total["median"]=null;
                if(count>0)
                {
                    var medianArgs=args.Concat(new object[]{"@offset",(count-1)/2,"@limit",count%2==0?2:1}).ToArray();
                    var values=Query("SELECT t.score AS score"+ScoreSource(scope)+" ORDER BY t.score,t.tool_result_id LIMIT @limit OFFSET @offset",cancellation,medianArgs);
                    total["median"]=values.Average(row=>Convert.ToDouble(row["score"]));
                }
                var byResult=Query("SELECT t.result,COUNT(*) AS count,MIN(t.score) AS min,MAX(t.score) AS max,AVG(t.score) AS mean"+ScoreSource(scope)+" GROUP BY t.result",cancellation,args);
                foreach(var result in byResult)
                {
                    long groupCount=Number(result,"count");result["median"]=null;
                    if(groupCount>0)
                    {
                        var medianArgs=args.Concat(new object[]{"@result",result["result"],"@offset",(groupCount-1)/2,"@limit",groupCount%2==0?2:1}).ToArray();
                        result["median"]=Query("SELECT t.score AS score"+ScoreSource(scope)+" AND t.result=@result ORDER BY t.score,t.tool_result_id LIMIT @limit OFFSET @offset",cancellation,medianArgs).Average(row=>Convert.ToDouble(row["score"]));
                    }
                }
                var bins=Query("SELECT MIN(19,MAX(0,CAST(t.score*20 AS INTEGER))) AS bin,t.result,COUNT(*) AS count"+ScoreSource(scope)+" GROUP BY bin,t.result ORDER BY bin,t.result",cancellation,args);
                return new {total,byResult,bins};
            }
        }

        internal object ScoreWindow(string tool,string position,string scope,bool exclude,double exclusion,double score,long id,int requestedSize,bool previous,CancellationToken cancellation)
        {
            if(!new[]{"TOOL_OK","TOOL_NG","ACTUAL_NG_TOOL_NG","ACTUAL_NG_TOOL_OK"}.Contains(scope))throw new InvalidDataException("Invalid score scope");
            if(double.IsNaN(score)||double.IsInfinity(score)||score < -1 || score>1 || double.IsNaN(exclusion)||double.IsInfinity(exclusion)||exclusion<0||exclusion>1)throw new InvalidDataException("Invalid score cursor");
            int size=Math.Max(1,Math.Min(500,requestedSize));string comparison=previous?"<":">",order=previous?" DESC":" ASC";
            lock(_sync)
            {
                var rows=Query(@"SELECT t.tool_result_id AS observationId,i.image_id AS imageId,i.cell_id AS cellId,i.position_key AS position,
i.capture_timestamp AS captureTimestamp,i.full_path AS fullPath,i.processed_path AS processedPath,i.source_file_name AS sourceFileName,
i.source_row_number AS sourceRowNumber,i.workspace_key AS workspaceKey,i.workspace_name AS workspaceName,t.result,t.score,t.overlay_path AS overlayPath,
EXISTS(SELECT 1 FROM actual_ng a WHERE a.cell=i.cell_id AND a.position=i.position_key AND (a.day=substr(COALESCE(i.capture_timestamp,''),1,10) OR a.wildcard=1)) AS hasActualImage"+
                    ScoreSource(scope)+" AND (t.score"+comparison+"@score OR (t.score=@score AND t.tool_result_id"+comparison+"@id)) ORDER BY t.score"+order+",t.tool_result_id"+order+" LIMIT @limit",
                    cancellation,"@tool",tool??"","@position",position=="ALL"?"":position??"","@scope",scope,"@exclude",exclude?1:0,"@exclusion",exclusion,"@score",score,"@id",id,"@limit",size+1);
                bool more=rows.Count>size;if(more)rows.RemoveAt(rows.Count-1);if(previous)rows.Reverse();
                var first=rows.FirstOrDefault();var last=rows.LastOrDefault();
                return new {records=rows,hasMore=more,nextScore=last==null?score:Convert.ToDouble(last["score"]),nextId=last==null?id:Convert.ToInt64(last["observationId"]),previousScore=first==null?score:Convert.ToDouble(first["score"]),previousId=first==null?id:Convert.ToInt64(first["observationId"])};
            }
        }

        internal object MissPage(string date,string position,string afterDay,string afterCell,int requestedSize,CancellationToken cancellation)
        {
            int size=Math.Max(1,Math.Min(500,requestedSize));
            lock(_sync)
            {
                var rows=Query(ActualKeys+@"SELECT c.day,c.cell AS cellId,c.position FROM actual_keys a
JOIN cells c ON c.day=a.day AND c.cell=a.cell AND c.position=a.position
WHERE "+FinalOk+@" AND (@date='' OR c.day=@date) AND c.position=@position
AND (c.day>@day OR (c.day=@day AND c.cell>@cell)) ORDER BY c.day,c.cell LIMIT @limit",
                    cancellation,"@date",date??"","@position",position??"","@day",afterDay??"","@cell",afterCell??"","@limit",size+1);
                bool more=rows.Count>size;if(more)rows.RemoveAt(rows.Count-1);
                foreach(var row in rows)row["tools"]=Query(@"SELECT tool,CASE WHEN base_ng=1 THEN 'NG' WHEN base_ok=1 THEN 'OK' ELSE 'UNKNOWN' END AS result,
CASE WHEN base_ng=1 THEN min_ng WHEN base_ok=1 THEN min_ok ELSE min_score END AS representativeScore,
COALESCE((SELECT value FROM thresholds h WHERE h.position=t.position AND h.tool=t.tool),.5) AS threshold
FROM tools t WHERE day=@day AND cell=@cell AND position=@position ORDER BY tool",cancellation,"@day",row["day"],"@cell",row["cellId"],"@position",row["position"]);
                var last=rows.LastOrDefault();
                return new {records=rows,hasMore=more,nextDay=last==null?afterDay:Convert.ToString(last["day"]),nextCell=last==null?afterCell:Convert.ToString(last["cellId"])};
            }
        }

        internal object CellDetail(string day,string cell,string position,long afterImageId,int requestedSize,CancellationToken cancellation)
        {
            int size=Math.Max(1,Math.Min(200,requestedSize));
            lock(_sync)
            {
                var rows=Query(@"SELECT i.image_id AS imageId,i.cell_id AS cellId,i.position_key AS position,i.capture_timestamp AS captureTimestamp,
i.full_path AS fullPath,i.processed_path AS processedPath,i.source_file_name AS sourceFileName,i.source_row_number AS sourceRowNumber,
i.workspace_name AS workspaceName,i.workspace_key AS workspaceKey,i.total_result AS totalResult
FROM source.images i WHERE i.run_id=@run AND i.cell_id=@cell AND i.position_key=@position
AND substr(COALESCE(i.capture_timestamp,''),1,10)=@day AND i.image_id>@after ORDER BY i.image_id LIMIT @limit",
                    cancellation,"@cell",cell??"","@position",position??"","@day",day??"","@after",afterImageId,"@limit",size+1);
                bool more=rows.Count>size;if(more)rows.RemoveAt(rows.Count-1);
                foreach(var row in rows)row["tools"]=Query("SELECT tool_result_id AS observationId,tool_name AS tool,result,score,overlay_path AS overlayPath FROM source.tool_results WHERE image_id=@image ORDER BY tool_result_id",cancellation,"@image",row["imageId"]);
                var tools=Query(@"SELECT tool,base_ng AS baseNg,base_ok AS baseOk,ng,min_score AS minScore,min_ng AS minNgScore,max_ng AS maxNgScore,min_ok AS minOkScore,
COALESCE((SELECT value FROM thresholds h WHERE h.position=t.position AND h.tool=t.tool),0.5) AS threshold
FROM tools t WHERE day=@day AND cell=@cell AND position=@position ORDER BY tool",cancellation,"@day",day??"","@cell",cell??"","@position",position??"");
                var images=Query("SELECT day,cell AS cellId,position,path AS fullPath,wildcard FROM actual_ng WHERE cell=@cell AND position=@position AND (day=@day OR wildcard=1) ORDER BY path LIMIT 201",cancellation,"@day",day??"","@cell",cell??"","@position",position??"");
                bool moreImages=images.Count>200;if(moreImages)images.RemoveAt(200);
                return new {records=rows,tools,actualImages=images,hasMoreImages=moreImages,hasMore=more,nextImageId=rows.Count==0?afterImageId:Convert.ToInt64(rows.Last()["imageId"])};
            }
        }

        public void Dispose() { lock (_sync) _connection.Dispose(); }
    }
}
