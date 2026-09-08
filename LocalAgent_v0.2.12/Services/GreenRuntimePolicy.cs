using System;
using System.Linq;
using System.Reflection;

namespace VisionQC.LocalAgent.Services
{
    // Capability-based access also handles SDKs before TensorRTMode was introduced.
    // Changes are in memory only; the caller must not cache this modified runtime.
    internal static class GreenRuntimePolicy
    {
        private static PropertyInfo Property(object value, string name)
        {
            if (value == null) return null;
            return value.GetType().GetProperty(name)
                ?? value.GetType().GetInterfaces().Select(t => t.GetProperty(name)).FirstOrDefault(p => p != null);
        }

        internal static string Describe(object parameters)
        {
            return string.Join(" | ", new[] { "TensorRTMode", "ProcessWithTrt", "HeatMapOn", "BatchSize", "ViewSize" }
                .Select(name => {
                    try { var p = Property(parameters, name); return name + "=" + (p == null ? "not exposed" : Convert.ToString(p.GetValue(parameters, null))); }
                    catch (Exception ex) { return name + "=unavailable(" + ex.GetType().Name + ")"; }
                }));
        }

        internal static string DisableTensorRt(object parameters)
        {
            var property = Property(parameters, "TensorRTMode");
            object disabled;
            if (property != null && property.PropertyType.IsEnum && Enum.IsDefined(property.PropertyType, "None"))
                disabled = Enum.Parse(property.PropertyType, "None");
            else
            {
                property = Property(parameters, "ProcessWithTrt");
                if (property == null || property.PropertyType != typeof(bool))
                    throw new InvalidOperationException("이 Tool/API에는 TensorRT 해제 기능이 노출되어 있지 않습니다. 호환 옵션을 해제하고 기본 실행을 사용하세요.");
                disabled = false;
            }
            var before = property.GetValue(parameters, null);
            if (!Equals(before, disabled))
            {
                if (!property.CanWrite) throw new InvalidOperationException("TensorRT 설정이 읽기 전용이라 호환 옵션을 적용하지 못했습니다.");
                property.SetValue(parameters, disabled, null);
            }
            if (!Equals(property.GetValue(parameters, null), disabled))
                throw new InvalidOperationException("TensorRT 해제 설정 검증에 실패했습니다. 검사를 시작하지 않습니다.");
            return property.Name + "=" + before + " -> " + disabled;
        }
    }
}
