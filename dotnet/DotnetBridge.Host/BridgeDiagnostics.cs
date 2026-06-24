using System;

namespace DotnetBridge.Host;

/// <summary>
/// The single observability seam for the bridge. Because the library runs as a guest
/// runtime with no console of its own — and because managed exceptions must never cross
/// the C ABI — every failure that would otherwise be swallowed is reported here instead
/// of vanishing. Assign <see cref="OnError"/> from your app to forward bridge-internal
/// errors to your logging stack (e.g. <c>os_log</c>); when it is unset, messages are
/// written to <see cref="Console.Error"/>, which surfaces in the device/system log.
/// </summary>
public static class BridgeDiagnostics
{
    /// <summary>
    /// Optional sink for bridge-internal errors, invoked as <c>(context, exception?)</c>.
    /// The exception is <see langword="null"/> for non-exceptional diagnostics. The handler
    /// must not throw; any exception it raises is caught and the message falls back to stderr.
    /// </summary>
    public static Action<string, Exception?>? OnError { get; set; }

    /// <summary>
    /// Report an internal error. Routes to <see cref="OnError"/> if set (falling back to
    /// stderr if that handler throws), otherwise writes to <see cref="Console.Error"/>.
    /// This method never throws.
    /// </summary>
    /// <param name="context">A short description of where/what failed.</param>
    /// <param name="ex">The associated exception, if any.</param>
    public static void Error(string context, Exception? ex = null)
    {
        var sink = OnError;
        if (sink is not null)
        {
            try { sink(context, ex); return; }
            catch { /* a broken logger must not break the bridge; fall through to stderr */ }
        }

        try
        {
            Console.Error.WriteLine(ex is null ? $"[dni] {context}" : $"[dni] {context}: {ex}");
        }
        catch { /* nothing more we can do; the diagnostics path must never throw */ }
    }
}
