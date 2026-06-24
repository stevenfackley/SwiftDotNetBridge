using System;
using System.Runtime.InteropServices;
using DotnetBridge.Host;

namespace DotnetBridge.Native;

/// <summary>
/// The C ABI surface. Every method is static, parameterless, blittable-return —
/// the NativeAOT export constraints. Managed exceptions never cross the boundary.
/// </summary>
public static class Exports
{
    [UnmanagedCallersOnly(EntryPoint = "dni_initialize")]
    public static int Initialize()
    {
        try { return BridgeRuntime.Initialize(Bootstrap.Modules()); }
        catch (Exception) { return -5; }
    }

    [UnmanagedCallersOnly(EntryPoint = "dni_http_start")]
    public static int HttpStart()
    {
        try { return BridgeRuntime.HttpStart(); }
        catch (Exception) { return -5; }
    }

    [UnmanagedCallersOnly(EntryPoint = "dni_http_stop")]
    public static int HttpStop()
    {
        try { return BridgeRuntime.HttpStop(); }
        catch (Exception) { return -5; }
    }

    [UnmanagedCallersOnly(EntryPoint = "dni_shutdown")]
    public static void Shutdown()
    {
        try { BridgeRuntime.Shutdown(); }
        catch (Exception) { /* never let exceptions escape the ABI */ }
    }
}
