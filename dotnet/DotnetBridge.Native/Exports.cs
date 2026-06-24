using System;
using System.Runtime.InteropServices;
using DotnetBridge.Host;

namespace DotnetBridge.Native;

/// <summary>
/// The C ABI surface. Every method is static, parameterless, blittable-return —
/// the NativeAOT export constraints. Managed exceptions never cross the boundary:
/// each export traps everything and maps it to <see cref="DniStatus.Internal"/>,
/// reporting the cause through <see cref="BridgeDiagnostics"/> so the failure is
/// observable instead of a silent <c>-5</c>.
/// </summary>
public static class Exports
{
    /// <summary>
    /// <c>dni_initialize</c>: builds the route table from the registered modules. Idempotent.
    /// </summary>
    /// <returns><see cref="DniStatus.Ok"/> on success, or <see cref="DniStatus.Internal"/> on failure.</returns>
    [UnmanagedCallersOnly(EntryPoint = "dni_initialize")]
    public static int Initialize()
    {
        try { return BridgeRuntime.Initialize(Bootstrap.Modules()); }
        catch (Exception ex) { BridgeDiagnostics.Error("dni_initialize failed", ex); return DniStatus.Internal; }
    }

    /// <summary>
    /// <c>dni_http_start</c>: binds the loopback server on 127.0.0.1:0 and starts accepting.
    /// Idempotent — returns the cached port if already running.
    /// </summary>
    /// <returns>The bound TCP port (&gt;0), or a negative <see cref="DniStatus"/> code.</returns>
    [UnmanagedCallersOnly(EntryPoint = "dni_http_start")]
    public static int HttpStart()
    {
        try { return BridgeRuntime.HttpStart(); }
        catch (Exception ex) { BridgeDiagnostics.Error("dni_http_start failed", ex); return DniStatus.Internal; }
    }

    /// <summary>
    /// <c>dni_http_stop</c>: stops the loopback listener. Safe to call when not running.
    /// </summary>
    /// <returns><see cref="DniStatus.Ok"/>, or <see cref="DniStatus.Internal"/> on failure.</returns>
    [UnmanagedCallersOnly(EntryPoint = "dni_http_stop")]
    public static int HttpStop()
    {
        try { return BridgeRuntime.HttpStop(); }
        catch (Exception ex) { BridgeDiagnostics.Error("dni_http_stop failed", ex); return DniStatus.Internal; }
    }

    /// <summary>
    /// <c>dni_shutdown</c>: releases the server. Call last. Never throws and returns no status.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "dni_shutdown")]
    public static void Shutdown()
    {
        // dni_shutdown must never throw or report a failure code — it is the last call.
        try { BridgeRuntime.Shutdown(); }
        catch (Exception ex) { BridgeDiagnostics.Error("dni_shutdown failed", ex); }
    }
}
