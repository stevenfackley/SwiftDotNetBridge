using System;

namespace DotnetBridge.Host;

/// <summary>
/// Memory-pressure coordination for the guest runtime. NativeAOT's GC does not react to iOS memory
/// warnings on its own, so the host wires the OS memory-pressure signal to <see cref="OnMemoryPressure"/>:
/// it forces a collection and flips the server into <see cref="Degraded"/> mode, where new connections
/// are shed with <c>503</c> until <see cref="Recover"/> is called. This trades availability for
/// survival — a few 503s beat a Jetsam OOM kill.
/// </summary>
public static class BridgeMemory
{
    private static volatile bool _degraded;

    /// <summary>When true, the server sheds new connections with <c>503</c>.</summary>
    public static bool Degraded
    {
        get => _degraded;
        set => _degraded = value;
    }

    /// <summary>
    /// Invoke from the OS memory-pressure handler: force a GC and enter degraded mode. Never throws.
    /// </summary>
    public static void OnMemoryPressure()
    {
        _degraded = true;
        try
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            BridgeDiagnostics.Error("memory pressure: forced GC, entered degraded mode");
        }
        catch { /* the pressure handler must never throw */ }
    }

    /// <summary>Leave degraded mode once pressure subsides.</summary>
    public static void Recover() => _degraded = false;
}
