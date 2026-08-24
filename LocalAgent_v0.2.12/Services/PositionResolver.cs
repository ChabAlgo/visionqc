using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace VisionQC.LocalAgent.Services
{
    internal static class PositionResolver
    {
        // 파일명에 Position 이름이 정확히 하나만 존재해야 단일 Green 검사가 어느 Workspace를 쓸지 결정할 수 있다.
        internal static PositionResolution Resolve(string imagePath, IEnumerable<AgentPositionRequest> positions)
        {
            string fileName = Path.GetFileNameWithoutExtension(imagePath ?? "") ?? "";
            var matches = (positions ?? Enumerable.Empty<AgentPositionRequest>())
                .Where(x => x != null && x.enabled && !string.IsNullOrWhiteSpace(x.displayName))
                .Where(x => fileName.IndexOf(x.displayName, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderByDescending(x => x.displayName.Length)
                .ToList();
            return new PositionResolution { matches = matches };
        }
    }

    internal sealed class PositionResolution
    {
        internal List<AgentPositionRequest> matches { get; set; } = new List<AgentPositionRequest>();
        internal bool IsUnique { get { return matches.Count == 1; } }
        internal AgentPositionRequest Position { get { return IsUnique ? matches[0] : null; } }
    }
}
