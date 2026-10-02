using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Data.SQLite;

namespace VisionQC.LocalAgent.Services
{
    internal sealed class PositionAliases
    {
        private readonly Dictionary<string,string> _cache = new Dictionary<string,string>(StringComparer.Ordinal);
        private readonly Dictionary<string,string> _names = new Dictionary<string,string>(StringComparer.Ordinal);
        internal static string Token(string value) {return Regex.Replace((value??"").Trim().ToUpperInvariant(), @"[^\p{L}\p{N}]", "");}
        internal PositionAliases(IEnumerable<PositionAliasDefinition> definitions)
        {
            foreach(var def in definitions??Enumerable.Empty<PositionAliasDefinition>())
            {
                if(def==null||string.IsNullOrWhiteSpace(def.name))throw new InvalidDataException("포지션 대표 이름이 없습니다.");
                foreach(string name in new[]{def.name}.Concat(def.aliases??new List<string>()))
                {
                    string token=Token(name), prior;
                    if(token.Length==0)throw new InvalidDataException("유효하지 않은 포지션 이름/별칭: "+name);
                    if(_names.TryGetValue(token,out prior)&&prior!=def.name)throw new InvalidDataException("포지션 별칭 충돌: "+name+" → "+prior+", "+def.name);
                    _names[token]=def.name;
                }
            }
        }
        internal static bool Active(IEnumerable<PositionAliasDefinition> definitions) {return definitions!=null&&definitions.Any(d=>d.aliases!=null&&d.aliases.Count>0);}
        internal string Canonical(string name) {if(name==null)return null;string result;if(_cache.TryGetValue(name,out result))return result;result=_names.TryGetValue(Token(name),out result)?result:name;if(_cache.Count<10000)_cache[name]=result;return result;}
        internal static bool FileMatches(string fileName,string name)
        {
            return !string.IsNullOrWhiteSpace(name)&&Regex.IsMatch(fileName??"",@"(?<![\p{L}\p{N}])"+Regex.Escape(name)+@"(?![\p{L}\p{N}])",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
        }
        // Read-only view: original history/log values are never rewritten.
        internal static void CreateView(SQLiteConnection connection, string schema, string view, IEnumerable<PositionAliasDefinition> definitions)
        {
            if((schema!="main"&&schema!="source")||(view!="images"&&view!="canonical_images"))throw new ArgumentException("view");
            var resolver=new PositionAliases(definitions);
            using(var command=connection.CreateCommand())
            {
                command.CommandText="CREATE TEMP TABLE IF NOT EXISTS vq_position_aliases(raw TEXT PRIMARY KEY,canonical TEXT NOT NULL);DELETE FROM temp.vq_position_aliases;";command.ExecuteNonQuery();
                var rawNames=new List<string>();
                command.CommandText="SELECT DISTINCT position_key FROM "+schema+".images WHERE position_key IS NOT NULL";
                using(var reader=command.ExecuteReader())while(reader.Read())rawNames.Add(reader.GetString(0));
                foreach(var raw in rawNames)
                {
                    string canonical=resolver.Canonical(raw);if(canonical==raw)continue;
                    command.CommandText="INSERT INTO temp.vq_position_aliases VALUES(@raw,@canonical)";
                    command.Parameters.Clear();command.Parameters.AddWithValue("@raw",raw);command.Parameters.AddWithValue("@canonical",canonical);command.ExecuteNonQuery();
                }
                var columns=new List<string>();command.Parameters.Clear();command.CommandText="PRAGMA "+schema+".table_info(images)";
                using(var reader=command.ExecuteReader())while(reader.Read()){string name=reader.GetString(1);columns.Add(name=="position_key"?"COALESCE(a.canonical,i.position_key) AS position_key":"i.["+name.Replace("]","]]")+"]");}
                command.CommandText="DROP VIEW IF EXISTS temp."+view+";CREATE TEMP VIEW "+view+" AS SELECT "+string.Join(",",columns)+" FROM "+schema+".images i LEFT JOIN temp.vq_position_aliases a ON a.raw=i.position_key;";command.ExecuteNonQuery();
            }
        }
    }
}
