using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Xml;
using System.Xml.Linq;

namespace VisionQC.LocalAgent.Services
{
    // Matches the former browser XLSX path: first worksheet, cached values, no formula evaluation.
    // Read one row at a time; the worksheet never becomes a complete XML DOM or row array.
    internal static class XlsxResultReader
    {
        internal sealed class TableRow { internal List<string> Values; internal int Number; }
        private static XmlReader Open(ZipArchiveEntry entry, long limit)
        {
            if (entry == null || entry.Length > limit) throw new InvalidDataException("XLSX 내부 파일이 없거나 읽기 한도를 초과했습니다.");
            return XmlReader.Create(entry.Open(), new XmlReaderSettings { DtdProcessing=DtdProcessing.Prohibit, XmlResolver=null, CloseInput=true, MaxCharactersInDocument=limit });
        }

        internal static IEnumerable<TableRow> Rows(string path, CancellationToken cancellation)
        {
            if (new FileInfo(path).Length > 50L*1024*1024) throw new InvalidDataException("XLSX는 파일당 50MB 이하를 지원합니다. 큰 파일은 CSV로 저장해 선택하세요.");
            using (var zip = ZipFile.OpenRead(path))
            {
                string relationshipId;
                using (var xml = Open(zip.GetEntry("xl/workbook.xml"), 1024*1024))
                {
                    var sheet=XDocument.Load(xml).Descendants().FirstOrDefault(e=>e.Name.LocalName=="sheet");
                    relationshipId=sheet?.Attributes().FirstOrDefault(a=>a.Name.LocalName=="id")?.Value;
                }
                string target;
                using (var xml = Open(zip.GetEntry("xl/_rels/workbook.xml.rels"), 1024*1024))
                    target=XDocument.Load(xml).Descendants().FirstOrDefault(e=>e.Name.LocalName=="Relationship" && (string)e.Attribute("Id")==relationshipId)?.Attribute("Target")?.Value;
                if(string.IsNullOrWhiteSpace(relationshipId)||string.IsNullOrWhiteSpace(target))throw new InvalidDataException("XLSX 첫 시트 경로를 찾지 못했습니다.");
                string sheetPath=target.StartsWith("/")?target.TrimStart('/'):"xl/"+(target.StartsWith("./")?target.Substring(2):target);
                // Resolve workbook-relative ZIP paths without extracting any file.
                var parts=new List<string>();
                foreach(string part in sheetPath.Split('/')) { if(part=="..") { if(parts.Count==0)throw new InvalidDataException("XLSX 시트 경로가 잘못되었습니다.");parts.RemoveAt(parts.Count-1); } else if(part.Length>0&&part!=".")parts.Add(part); }
                sheetPath=string.Join("/",parts);
                var shared=new List<string>();
                var strings=zip.GetEntry("xl/sharedStrings.xml");
                if(strings!=null)using(var xml=Open(strings,128L*1024*1024))
                {
                    while(xml.Read())if(xml.NodeType==XmlNodeType.Element&&xml.LocalName=="si")
                    {
                        cancellation.ThrowIfCancellationRequested();
                        using(var subtree=xml.ReadSubtree())shared.Add(string.Concat(XElement.Load(subtree).Descendants().Where(e=>e.Name.LocalName=="t").Select(e=>e.Value)));
                    }
                }
                using(var xml=Open(zip.GetEntry(sheetPath),512L*1024*1024))
                {
                    int width=0,rowNumber=0;bool headerFound=false;
                    while(xml.Read())if(xml.NodeType==XmlNodeType.Element&&xml.LocalName=="row")
                    {
                        cancellation.ThrowIfCancellationRequested();
                        XElement row;using(var subtree=xml.ReadSubtree())row=XElement.Load(subtree);
                        int number;if(!int.TryParse((string)row.Attribute("r"),NumberStyles.None,CultureInfo.InvariantCulture,out number)||number<1)number=rowNumber+1;rowNumber=number;
                        var values=new List<string>();int next=0;
                        foreach(var cell in row.Elements().Where(e=>e.Name.LocalName=="c"))
                        {
                            string reference=(string)cell.Attribute("r")??"";int column=0;
                            foreach(char ch in reference.TakeWhile(char.IsLetter))column=checked(column*26+char.ToUpperInvariant(ch)-'A'+1);
                            column=column==0?next:column-1;
                            if(column>=16384)throw new InvalidDataException("XLSX 열 범위를 초과했습니다.");
                            while(values.Count<=column)values.Add("");
                            string type=(string)cell.Attribute("t")??"";
                            string value=cell.Elements().FirstOrDefault(e=>e.Name.LocalName=="v")?.Value??"";
                            if(type=="s") { int index;if(!int.TryParse(value,NumberStyles.None,CultureInfo.InvariantCulture,out index)||index<0||index>=shared.Count)throw new InvalidDataException("XLSX 문자열 참조가 잘못되었습니다.");value=shared[index]; }
                            else if(type=="inlineStr")value=string.Concat(cell.Descendants().Where(e=>e.Name.LocalName=="t").Select(e=>e.Value));
                            else if(type=="b")value=value=="1"?"true":"false";
                            else if(type=="e")throw new InvalidDataException("XLSX 수식 오류가 포함되어 있습니다.");
                            values[column]=value;next=column+1;
                        }
                        if(values.All(string.IsNullOrWhiteSpace))continue;
                        if(!headerFound)
                        {
                            var keys=values.Select(v=>v.Trim().Replace(" ","").ToLowerInvariant()).ToList();
                            if(!keys.Contains("cellid")||!keys.Contains("total_result"))continue;
                            if(keys.Any(string.IsNullOrWhiteSpace)||keys.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=keys.Count)throw new InvalidDataException("XLSX 열 이름이 비어 있거나 중복되었습니다.");
                            width=values.Count;headerFound=true;
                        }
                        else { if(values.Count>width)throw new InvalidDataException("XLSX 데이터 열 수가 헤더보다 많습니다.");while(values.Count<width)values.Add(""); }
                        yield return new TableRow{Values=values,Number=rowNumber};
                    }
                    if(!headerFound)throw new InvalidDataException("XLSX에서 Cell ID와 Total_result 열을 찾지 못했습니다.");
                }
            }
        }
    }
}
