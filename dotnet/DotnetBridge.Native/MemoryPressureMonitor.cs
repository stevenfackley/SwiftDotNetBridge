using System;
using System.Runtime.InteropServices;
using DotnetBridge.Host;

namespace DotnetBridge.Native;

/// <summary>
/// Wires the macOS/iOS dispatch memory-pressure source to <see cref="BridgeMemory.OnMemoryPressure"/>.
/// NativeAOT's GC doesn't react to iOS memory warnings on its own, so without this a low-memory
/// condition silently risks a Jetsam OOM kill. No-op on non-Apple platforms. Best-effort: any
/// registration failure is swallowed (the bridge simply won't auto-degrade).
/// </summary>
internal static unsafe class MemoryPressureMonitor
{
    private const string LibSystem = "/usr/lib/libSystem.dylib";

    private static IntPtr _source;

    /// <summary>Register the OS memory-pressure handler (idempotent; Apple platforms only).</summary>
    public static void Register()
    {
        if (_source != IntPtr.Zero) return;   // already registered
        if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsIOS() && !OperatingSystem.IsMacCatalyst())
            return;

        try
        {
            var lib = NativeLibrary.Load(LibSystem);
            // dlsym name has no leading underscore. This is the dispatch_source_type for memory pressure.
            var type = NativeLibrary.GetExport(lib, "dispatch_source_type_memorypressure");
            if (type == IntPtr.Zero) return;

            var queue = dispatch_get_global_queue(0 /* DISPATCH_QUEUE_PRIORITY_DEFAULT */, 0);
            // mask = DISPATCH_MEMORYPRESSURE_WARN (0x02) | DISPATCH_MEMORYPRESSURE_CRITICAL (0x04)
            var source = dispatch_source_create(type, 0, (nuint)0x06, queue);
            if (source == IntPtr.Zero) return;

            dispatch_source_set_event_handler_f(source, &OnPressure);
            dispatch_resume(source);
            _source = source;
        }
        catch (Exception ex)
        {
            BridgeDiagnostics.Error("memory-pressure monitor registration failed", ex);
        }
    }

    [UnmanagedCallersOnly]
    private static void OnPressure(IntPtr context) => BridgeMemory.OnMemoryPressure();

    [DllImport(LibSystem)]
    private static extern IntPtr dispatch_get_global_queue(nint identifier, nuint flags);

    [DllImport(LibSystem)]
    private static extern IntPtr dispatch_source_create(IntPtr type, nuint handle, nuint mask, IntPtr queue);

    [DllImport(LibSystem)]
    private static extern void dispatch_source_set_event_handler_f(IntPtr source, delegate* unmanaged<IntPtr, void> handler);

    [DllImport(LibSystem)]
    private static extern void dispatch_resume(IntPtr source);
}
