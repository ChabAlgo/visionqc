using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using VisionQC.LocalAgent.Domain;

namespace VisionQC.LocalAgent.Services
{
    internal static class NamingProfileParser
    {
        internal static NamingPreviewResponse Preview(NamingPreviewRequest request)
        {
            var response = new NamingPreviewResponse();
            if (request == null || request.profile == null)
            {
                response.ok = false;
                response.error = "파일명 규칙이 없습니다.";
                return response;
            }

            string validation = ValidateProfile(request.profile);
            if (!string.IsNullOrEmpty(validation))
            {
                response.ok = false;
                response.error = validation;
                return response;
            }

            response.ok = true;
            response.profile = request.profile;
            foreach (string fileName in request.fileNames ?? new List<string>())
            {
                NamingParseResult result = Parse(request.profile, fileName);
                response.records.Add(result);
                if (result.status == "success") response.successCount++;
                else if (result.status == "partial") response.partialCount++;
                else if (result.status == "ambiguous") response.ambiguousCount++;
                else response.failedCount++;
            }
            return response;
        }

        internal static NamingParseResult Parse(NamingProfile profile, string pathOrFileName)
        {
            string fileName = Path.GetFileName(pathOrFileName ?? "");
            string stem = Path.GetFileNameWithoutExtension(fileName ?? "") ?? "";
            string delimiter = string.IsNullOrEmpty(profile.delimiter) ? "_" : profile.delimiter;
            List<string> tokens = stem.Split(new[] { delimiter }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
            var result = new NamingParseResult { fileName = fileName, tokens = tokens };

            bool cellAmbiguous;
            result.cellIdRaw = ExtractCell(profile.cellId, tokens, out cellAmbiguous, result.warnings);
            if (!string.IsNullOrEmpty(result.cellIdRaw))
            {
                int take = profile.cellId.extractLength > 0 ? profile.cellId.extractLength : result.cellIdRaw.Length;
                result.cellId = result.cellIdRaw.Substring(0, Math.Min(take, result.cellIdRaw.Length)).Trim().ToUpperInvariant();
            }

            bool dateAmbiguous;
            DateTime date;
            if (TryExtractDate(profile.date, tokens, out date, out dateAmbiguous)) result.captureDate = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            else if (dateAmbiguous) result.warnings.Add("날짜 후보가 여러 개입니다.");
            else result.warnings.Add("날짜를 추출하지 못했습니다.");

            bool timeAmbiguous;
            TimeSpan time;
            if (TryExtractTime(profile.time, tokens, out time, out timeAmbiguous)) result.captureTime = time.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
            else if (timeAmbiguous) result.warnings.Add("시간 후보가 여러 개입니다.");
            else result.warnings.Add("시간을 추출하지 못했습니다.");

            if (!string.IsNullOrEmpty(result.captureDate) && !string.IsNullOrEmpty(result.captureTime))
                result.captureTimestamp = result.captureDate + "T" + result.captureTime;

            if (cellAmbiguous || dateAmbiguous || timeAmbiguous) result.status = "ambiguous";
            else if (string.IsNullOrEmpty(result.cellId))
            {
                result.warnings.Add("Cell ID를 추출하지 못했습니다.");
                result.status = "failed";
            }
            else if (string.IsNullOrEmpty(result.captureDate) || string.IsNullOrEmpty(result.captureTime)) result.status = "partial";
            else result.status = "success";
            return result;
        }

        private static string ValidateProfile(NamingProfile profile)
        {
            if (profile.cellId == null || profile.date == null || profile.time == null) return "Cell ID, 날짜, 시간 규칙을 모두 지정하세요.";
            if (profile.delimiter == null || profile.delimiter.Length == 0) return "구분자는 비워 둘 수 없습니다.";
            if (IsTokenMode(profile.cellId) && profile.cellId.tokenIndex < 1) return "Cell ID 토큰 번호는 1 이상이어야 합니다.";
            if (IsTokenMode(profile.date) && profile.date.tokenIndex < 1) return "날짜 토큰 번호는 1 이상이어야 합니다.";
            if (IsTokenMode(profile.time) && profile.time.tokenIndex < 1) return "시간 토큰 번호는 1 이상이어야 합니다.";
            if (!IsTokenMode(profile.cellId) && profile.cellId.candidateLength < 1) return "자동 Cell ID 모드에는 후보 토큰 길이가 필요합니다.";
            if (profile.cellId.extractLength < 1) return "Cell ID 추출 길이는 1 이상이어야 합니다.";
            if (profile.cellId.candidateLength > 0 && profile.cellId.extractLength > profile.cellId.candidateLength) return "Cell ID 추출 길이는 후보 토큰 길이보다 클 수 없습니다.";
            return null;
        }

        private static string ExtractCell(NamingFieldRule rule, List<string> tokens, out bool ambiguous, List<string> warnings)
        {
            ambiguous = false;
            if (IsTokenMode(rule))
            {
                string token = TokenAt(tokens, rule.tokenIndex);
                if (string.IsNullOrEmpty(token)) return null;
                if (!IsCellCandidate(token, rule))
                {
                    warnings.Add("지정한 Cell ID 토큰이 조건에 맞지 않습니다.");
                    return null;
                }
                return token;
            }

            List<string> candidates = tokens.Where(token => IsCellCandidate(token, rule)).ToList();
            if (candidates.Count == 1) return candidates[0];
            if (candidates.Count > 1) ambiguous = true;
            return null;
        }

        private static bool TryExtractDate(NamingFieldRule rule, List<string> tokens, out DateTime date, out bool ambiguous)
        {
            ambiguous = false;
            date = default(DateTime);
            IEnumerable<string> candidates;
            if (IsTokenMode(rule)) candidates = new[] { TokenAt(tokens, rule.tokenIndex) };
            else candidates = tokens;
            List<DateTime> valid = candidates.Where(x => !string.IsNullOrEmpty(x)).Select(ParseDate).Where(x => x.HasValue).Select(x => x.Value).ToList();
            if (valid.Count == 1) { date = valid[0]; return true; }
            if (valid.Count > 1) ambiguous = true;
            return false;
        }

        private static bool TryExtractTime(NamingFieldRule rule, List<string> tokens, out TimeSpan time, out bool ambiguous)
        {
            ambiguous = false;
            time = default(TimeSpan);
            IEnumerable<string> candidates;
            if (IsTokenMode(rule)) candidates = new[] { TokenAt(tokens, rule.tokenIndex) };
            else candidates = tokens;
            List<TimeSpan> valid = candidates.Where(x => !string.IsNullOrEmpty(x)).Select(ParseTime).Where(x => x.HasValue).Select(x => x.Value).ToList();
            if (valid.Count == 1) { time = valid[0]; return true; }
            if (valid.Count > 1) ambiguous = true;
            return false;
        }

        private static DateTime? ParseDate(string value)
        {
            DateTime date;
            return DateTime.TryParseExact(value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date) ? date : (DateTime?)null;
        }

        private static TimeSpan? ParseTime(string value)
        {
            DateTime time;
            return DateTime.TryParseExact(value, "HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out time) ? time.TimeOfDay : (TimeSpan?)null;
        }

        private static bool IsCellCandidate(string token, NamingFieldRule rule)
        {
            if (string.IsNullOrWhiteSpace(token)) return false;
            if (rule.candidateLength > 0 && token.Length != rule.candidateLength) return false;
            if (rule.requireLetter && !token.Any(char.IsLetter)) return false;
            return token.All(char.IsLetterOrDigit);
        }

        private static bool IsTokenMode(NamingFieldRule rule)
        {
            return string.Equals(rule.mode, "token", StringComparison.OrdinalIgnoreCase);
        }

        private static string TokenAt(List<string> tokens, int oneBasedIndex)
        {
            int index = oneBasedIndex - 1;
            return index >= 0 && index < tokens.Count ? tokens[index] : null;
        }
    }
}
