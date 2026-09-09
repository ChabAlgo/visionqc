using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using ViDi2;
using LocalRuntime = ViDi2.Runtime.Local;

namespace VisionQC.LocalAgent.Services
{
    internal static class GreenRuntimeFactory
    {
        private sealed class Origin { internal int Thread; internal string Id; }
        private static readonly ConditionalWeakTable<LocalRuntime.Control, Origin> Origins =
            new ConditionalWeakTable<LocalRuntime.Control, Origin>();

        internal static LocalRuntime.Control CreateOriginal(GpuMode mode, List<int> devices, bool detailed)
        {
            // Exact constructor used by DL_Simulation v1.13. No SDK debug overload,
            // memory policy override, metadata Control, or TensorRT parameter mutation.
            var control = AgentDiagnostics.Measure("Runtime.Create", "Original process | Mode=" + mode
                + " | RequestedDevices=" + string.Join(",", devices), detailed,
                () => new LocalRuntime.Control(mode, devices));
            Origins.Add(control, new Origin { Thread = Thread.CurrentThread.ManagedThreadId, Id = Guid.NewGuid().ToString("N") });
            AgentDiagnostics.Write("ORIGINAL_RUNTIME", Describe(control) + " | Original constructor; SDK policy unchanged");
            return control;
        }

        internal static LocalRuntime.Control Create(GpuMode mode, List<int> devices, bool detailed, bool disableMemoryPool, string purpose)
        {
            string context = purpose + " | Mode=" + mode + " | RequestedDevices=" + string.Join(",", devices)
                + " | SdkDebug=" + detailed + " | DisableOptimizedGpuMemory=" + disableMemoryPool;
            if (detailed) AgentDiagnostics.CaptureEnvironment("before-runtime-create");
            // Same GPU mode/device list and eager initialization as the original constructor.
            // Four-argument SDK overload adds only SDK debug logging; no Deferred mode or extra initialization.
            LocalRuntime.Control control = AgentDiagnostics.Measure("Runtime.Create", context, detailed, () =>
                detailed ? new LocalRuntime.Control(new LocalRuntime.LibraryAccess(), mode, devices, true)
                         : new LocalRuntime.Control(mode, devices));
            Origins.Add(control, new Origin { Thread = Thread.CurrentThread.ManagedThreadId, Id = Guid.NewGuid().ToString("N") });
            try
            {
                if (disableMemoryPool)
                    AgentDiagnostics.Measure("Runtime.OptimizedGPUMemory", Describe(control) + " | RequestedBytes=0", true,
                        () => control.OptimizedGPUMemory(0));
                AgentDiagnostics.Write("GPU_MEMORY_POLICY", Describe(control) + " | "
                    + (disableMemoryPool ? "Explicit OptimizedGPUMemory(0) call succeeded"
                        : "SDK default unchanged; actual reserved pool size is not queried"));
                if (detailed) AgentDiagnostics.CaptureEnvironment("after-runtime-create");
                return control;
            }
            catch
            {
                // Cleanup cannot replace the original memory-policy failure.
                try { control.Dispose(); } catch { }
                throw;
            }
        }

        internal static string Describe(LocalRuntime.Control control)
        {
            Origin origin;
            return AgentDiagnostics.Identity(control) + (control != null && Origins.TryGetValue(control, out origin)
                ? " | RuntimeId=" + origin.Id + " | CreateThread=" + origin.Thread : " | CreateThread=untracked")
                + " | CurrentThread=" + Thread.CurrentThread.ManagedThreadId;
        }
    }
}
