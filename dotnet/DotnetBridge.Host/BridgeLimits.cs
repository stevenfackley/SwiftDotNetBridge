using System;

namespace DotnetBridge.Host;

/// <summary>
/// Defensive bounds for the hand-rolled HTTP reader. Even though the listener is
/// loopback-only, a buggy or hostile in-process client can otherwise force unbounded
/// allocation (an enormous <c>Content-Length</c>, an endless header line) or pin a
/// connection open forever (a stalled half-request). These caps convert those into
/// clean <c>413</c>/<c>431</c>/timeout failures rather than an OOM kill or a leaked task.
/// Set any of these before calling <c>dni_http_start</c> if your payloads are larger.
/// </summary>
public static class BridgeLimits
{
    /// <summary>Maximum bytes allowed for a single request line or header line. Default 8&#160;KiB.</summary>
    public static int MaxLineBytes { get; set; } = 8 * 1024;

    /// <summary>Maximum number of request headers accepted. Default 100.</summary>
    public static int MaxHeaderCount { get; set; } = 100;

    /// <summary>Maximum request body size in bytes. Default 16&#160;MiB.</summary>
    public static int MaxBodyBytes { get; set; } = 16 * 1024 * 1024;

    /// <summary>
    /// Maximum time allowed to read one complete request before the connection is dropped.
    /// Guards against slow/stalled clients holding a connection (and its task) open. Default 30&#160;s.
    /// </summary>
    public static TimeSpan ReadTimeout { get; set; } = TimeSpan.FromSeconds(30);
}
