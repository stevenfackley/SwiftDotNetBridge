using System;

namespace DotnetBridge.Host;

/// <summary>Classifies a bridge-internal failure so diagnostics can be filtered or aggregated.</summary>
public enum BridgeErrorClass
{
    /// <summary>Uncategorized.</summary>
    Unknown,

    /// <summary>A malformed / oversized / unauthorized request (4xx-shaped).</summary>
    Protocol,

    /// <summary>A connection-level failure (socket reset, read timeout, write failure).</summary>
    Connection,

    /// <summary>The accept loop faulted.</summary>
    AcceptLoop,

    /// <summary>A route handler threw.</summary>
    Handler,

    /// <summary>An ABI lifecycle export (initialize/start/stop/shutdown) failed.</summary>
    Lifecycle,
}

/// <summary>
/// The single observability seam for the bridge. Because the library runs as a guest runtime with no
/// console of its own — and because managed exceptions must never cross the C ABI — every failure that
/// would otherwise be swallowed is reported here, tagged with a <see cref="BridgeErrorClass"/>, instead
/// of vanishing. Assign <see cref="OnError"/> from your app to forward these into your logging stack
/// (e.g. <c>os_log</c>); when unset, messages go to <see cref="Console.Error"/>.
/// </summary>
public static class BridgeDiagnostics
{
    /// <summary>
    /// Optional sink for bridge-internal errors, invoked as <c>(class, context, exception?)</c>. The
    /// exception is <see langword="null"/> for non-exceptional diagnostics. The handler must not throw;
    /// any exception it raises is caught and the message falls back to stderr.
    /// </summary>
    public static Action<BridgeErrorClass, string, Exception?>? OnError { get; set; }

    /// <summary>Report a classified internal error. Routes to <see cref="OnError"/> or stderr. Never throws.</summary>
    /// <param name="errorClass">The failure category.</param>
    /// <param name="context">A short description of where/what failed.</param>
    /// <param name="ex">The associated exception, if any.</param>
    public static void Error(BridgeErrorClass errorClass, string context, Exception? ex = null)
    {
        var sink = OnError;
        if (sink is not null)
        {
            try { sink(errorClass, context, ex); return; }
            catch { /* a broken logger must not break the bridge; fall through to stderr */ }
        }

        try
        {
            Console.Error.WriteLine(ex is null
                ? $"[dni:{errorClass}] {context}"
                : $"[dni:{errorClass}] {context}: {ex}");
        }
        catch { /* nothing more we can do; the diagnostics path must never throw */ }
    }

    /// <summary>Report an unclassified internal error (back-compat overload; class = <see cref="BridgeErrorClass.Unknown"/>).</summary>
    /// <param name="context">A short description of where/what failed.</param>
    /// <param name="ex">The associated exception, if any.</param>
    public static void Error(string context, Exception? ex = null)
        => Error(BridgeErrorClass.Unknown, context, ex);
}
