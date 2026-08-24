using System;
using System.Collections.Generic;

namespace VisionQC.LocalAgent.Domain
{
    // 파일명 규칙은 공정/Position별로 저장되며, 실행 이력에는 Profile ID와 버전을 함께 남긴다.
    public sealed class NamingProfile
    {
        public string id { get; set; }
        public string name { get; set; }
        public int version { get; set; } = 1;
        public string delimiter { get; set; } = "_";
        public NamingFieldRule cellId { get; set; } = new NamingFieldRule { mode = "auto", candidateLength = 18, extractLength = 16, requireLetter = true };
        public NamingFieldRule date { get; set; } = new NamingFieldRule { mode = "auto", format = "YYYYMMDD" };
        public NamingFieldRule time { get; set; } = new NamingFieldRule { mode = "auto", format = "HHMMSS" };
    }

    public sealed class NamingFieldRule
    {
        // token: 구분자 기준 N번째 토큰, auto: 조건에 맞는 토큰을 자동 탐색한다.
        public string mode { get; set; } = "auto";
        // UI와 API에서는 사람이 읽는 1부터 시작하는 번호를 사용한다.
        public int tokenIndex { get; set; }
        // Cell ID 후보 토큰의 전체 길이와 저장할 앞부분 길이.
        public int candidateLength { get; set; }
        public int extractLength { get; set; }
        public bool requireLetter { get; set; }
        public string format { get; set; }
    }

    public sealed class NamingPreviewRequest
    {
        public NamingProfile profile { get; set; }
        public List<string> fileNames { get; set; } = new List<string>();
    }

    public sealed class NamingPreviewResponse
    {
        public bool ok { get; set; }
        public string error { get; set; }
        public NamingProfile profile { get; set; }
        public List<NamingParseResult> records { get; set; } = new List<NamingParseResult>();
        public int successCount { get; set; }
        public int partialCount { get; set; }
        public int failedCount { get; set; }
        public int ambiguousCount { get; set; }
    }

    public sealed class NamingParseResult
    {
        public string fileName { get; set; }
        public List<string> tokens { get; set; } = new List<string>();
        public string cellIdRaw { get; set; }
        public string cellId { get; set; }
        public string captureDate { get; set; }
        public string captureTime { get; set; }
        public string captureTimestamp { get; set; }
        public string status { get; set; }
        public List<string> warnings { get; set; } = new List<string>();
    }
}
