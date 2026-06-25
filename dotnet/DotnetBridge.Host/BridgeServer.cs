using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using DotnetBridge.Abstractions;

namespace DotnetBridge.Host;

/// <summary>
/// Loopback HTTP/1.1 server. Binds 127.0.0.1:0 (OS-assigned ephemeral port),
/// one Task per connection, Connection: close per request. No Kestrel/ASP.NET.
/// </summary>
public sealed class BridgeServer : IDisposable
{
    /// <summary>Upper bound on bytes drained from a rejected request before closing (see <see cref="DrainAsync"/>).</summary>
    private const int DrainCapBytes = 64 * 1024;

    private readonly RouteTable _routes;
    private readonly object _gate = new();
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private int _port;
    private int _activeConnections;
    private int _generation;   // bumped on each start/stop; identifies the current listener epoch

    /// <summary>Create a server that dispatches requests against the given route table.</summary>
    public BridgeServer(RouteTable routes) => _routes = routes;

    /// <summary>Starts (idempotent). Returns the bound port (&gt;0).</summary>
    public int Start()
    {
        lock (_gate)
        {
            if (_listener is not null) return _port;
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            _port = ((IPEndPoint)listener.LocalEndpoint).Port;
            _cts = new CancellationTokenSource();
            _listener = listener;
            var generation = ++_generation;
            _ = AcceptLoopAsync(listener, generation, _cts.Token);
            return _port;
        }
    }

    /// <summary>
    /// Stops accepting immediately, lets in-flight requests finish within
    /// <see cref="BridgeLimits.GracefulStopTimeout"/>, then aborts the remainder. Idempotent.
    /// </summary>
    public void Stop()
    {
        CancellationTokenSource? cts;
        lock (_gate)
        {
            if (_listener is null) { _cts?.Dispose(); _cts = null; return; }
            _listener.Stop();          // stop accepting new connections immediately
            _listener = null;
            cts = _cts;
            _cts = null;
            _port = 0;
            _generation++;             // close the epoch so a stale accept-loop can't reset state
        }

        // Give in-flight requests a bounded window to finish before aborting the remainder.
        var deadline = Environment.TickCount64 + (long)BridgeLimits.GracefulStopTimeout.TotalMilliseconds;
        while (Volatile.Read(ref _activeConnections) > 0 && Environment.TickCount64 < deadline)
            Thread.Sleep(10);

        cts?.Cancel();
        cts?.Dispose();
    }

    /// <summary>Stops the server and releases its resources.</summary>
    public void Dispose() => Stop();

    private async Task AcceptLoopAsync(TcpListener listener, int generation, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { break; }   // expected: Stop()
                catch (ObjectDisposedException) { break; }      // expected: listener stopped
                catch (Exception ex)
                {
                    // Unexpected, possibly transient. Don't spin a tight failing loop —
                    // exit and let the finally reset state so the next Start() re-binds.
                    BridgeDiagnostics.Error("accept loop fault", ex);
                    break;
                }

                _ = HandleConnectionAsync(client, ct);
            }
        }
        finally
        {
            ResetIfFaulted(generation, ct);
        }
    }

    /// <summary>
    /// Admission control around <see cref="HandleClientAsync"/>: caps simultaneous connections at
    /// <see cref="BridgeLimits.MaxConcurrentConnections"/> and sheds the excess with a 503 instead
    /// of spawning unbounded per-connection tasks under a burst.
    /// </summary>
    private async Task HandleConnectionAsync(TcpClient client, CancellationToken ct)
    {
        // Under memory pressure, shed everything with 503 rather than risk a Jetsam kill.
        if (BridgeMemory.Degraded)
        {
            await RejectSaturatedAsync(client, ct).ConfigureAwait(false);
            return;
        }

        if (Interlocked.Increment(ref _activeConnections) > BridgeLimits.MaxConcurrentConnections)
        {
            Interlocked.Decrement(ref _activeConnections);
            await RejectSaturatedAsync(client, ct).ConfigureAwait(false);
            return;
        }

        try { await HandleClientAsync(client, ct).ConfigureAwait(false); }
        finally { Interlocked.Decrement(ref _activeConnections); }
    }

    private static async Task RejectSaturatedAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        {
            try
            {
                var stream = client.GetStream();
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(BridgeLimits.ReadTimeout);

                // Consume the (bounded) request before responding: closing a socket with unread
                // inbound bytes sends a TCP reset that would nuke the 503 before the client reads it.
                try { await HttpRequestParser.ReadAsync(stream, cts.Token).ConfigureAwait(false); }
                catch { /* malformed/oversized request: shed it anyway */ }

                await HttpResponseWriter.WriteAsync(stream,
                    BridgeResponse.Text("Service Unavailable", 503), cts.Token).ConfigureAwait(false);
            }
            catch (Exception ex) { BridgeDiagnostics.Error("saturation reject failed", ex); }
        }
    }

    /// <summary>
    /// If the accept loop exited for any reason OTHER than an explicit <see cref="Stop"/>,
    /// tear down the listener state so the server stops reporting a dead, stale port. The
    /// next <see cref="Start"/> (Swift calls it before every request) then re-binds to a
    /// fresh, working port — turning a previously-unrecoverable wedge into self-healing.
    /// </summary>
    private void ResetIfFaulted(int generation, CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return;                 // Stop() already cleaned up
        lock (_gate)
        {
            if (_generation != generation) return;              // a newer epoch owns the server now
            try { _listener?.Stop(); } catch { /* best effort */ }
            _listener = null;
            _cts?.Dispose();
            _cts = null;
            _port = 0;
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        {
            try
            {
                var stream = client.GetStream();

                // Bound the time spent reading one request so a stalled client can't pin
                // the connection (and its task) open indefinitely.
                using var readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                readCts.CancelAfter(BridgeLimits.ReadTimeout);

                BridgeResponse resp;
                try
                {
                    var req = await HttpRequestParser.ReadAsync(stream, readCts.Token).ConfigureAwait(false);
                    if (req is null) return;   // client closed / empty — nothing to answer

                    resp = await DispatchAsync(req, ct).ConfigureAwait(false);
                }
                catch (BridgeProtocolException pex)
                {
                    // A bad/oversized request: answer with the carried 4xx, no internal detail.
                    BridgeDiagnostics.Error("protocol error " + pex.StatusCode + ": " + pex.Message);

                    // The client may still be transmitting the rejected body. Bounded-drain it so
                    // the client can read our response cleanly instead of getting a TCP reset on
                    // close — but cap the drain so a huge body can't turn draining into a DoS.
                    await DrainAsync(stream, Math.Min(pex.PendingBodyBytes, DrainCapBytes), readCts.Token)
                        .ConfigureAwait(false);

                    resp = BridgeResponse.Text(ReasonText(pex.StatusCode), pex.StatusCode);
                }

                await HttpResponseWriter.WriteAsync(stream, resp, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Read timeout (the linked token fired, not a server stop): the client stalled.
                BridgeDiagnostics.Error("connection read timed out");
            }
            catch (OperationCanceledException)
            {
                // Server is stopping — expected; drop the connection quietly.
            }
            catch (Exception ex)
            {
                // Connection-level failure (socket reset mid-write, etc.). Log, never crash.
                BridgeDiagnostics.Error("connection handling failed", ex);
            }
        }
    }

    private async Task<BridgeResponse> DispatchAsync(BridgeRequest req, CancellationToken ct)
    {
        // Origin binding: only serve requests addressed to our loopback authority (DNS-rebind defense).
        if (!IsLoopbackHost(req))
            return BridgeResponse.Text("Bad Request", 400);

        // Capability-token auth (when a token is configured): require a valid X-DNI-Auth header.
        if (BridgeAuth.IsEnabled)
        {
            req.Headers.TryGetValue("X-DNI-Auth", out var presented);
            if (!BridgeAuth.IsAuthorized(presented))
                return BridgeResponse.Text("Unauthorized", 401);
        }

        var match = _routes.Match(req.Method, req.Path);
        if (match is null)
        {
            // Distinguish "no such resource" (404) from "wrong method on a known path" (405).
            return _routes.IsKnownPath(req.Path)
                ? BridgeResponse.Text("Method Not Allowed", 405)
                : BridgeResponse.NotFound();
        }

        var withValues = new BridgeRequest(req.Method, req.Path, req.RawQuery,
            req.Headers, req.Body, match.Value.Values);
        try
        {
            return await match.Value.Handler(withValues, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;   // cancellation is not a handler fault; let the caller treat it as expected
        }
        catch (Exception ex)
        {
            // A handler threw — that's our fault → 500, but never leak the message or stack to
            // the client. The full detail goes to diagnostics instead.
            BridgeDiagnostics.Error("handler error for " + req.Method + " " + req.Path, ex);
            return BridgeResponse.Text("Internal Server Error", 500);
        }
    }

    /// <summary>
    /// Best-effort read-and-discard of up to <paramref name="count"/> bytes (the rejected
    /// request body) so the peer can finish sending and read our error response without a
    /// reset. Reuses a small scratch buffer; never throws. Bounded by the caller so an
    /// oversized body cannot make the drain itself expensive.
    /// </summary>
    private static async Task DrainAsync(Stream stream, int count, CancellationToken ct)
    {
        if (count <= 0) return;
        var scratch = new byte[Math.Min(count, 8192)];
        var remaining = count;
        try
        {
            while (remaining > 0)
            {
                var n = await stream.ReadAsync(scratch.AsMemory(0, Math.Min(scratch.Length, remaining)), ct)
                    .ConfigureAwait(false);
                if (n == 0) break;   // peer stopped early / closed
                remaining -= n;
            }
        }
        catch { /* best effort: if draining fails, we still send the response and close */ }
    }

    private static bool IsLoopbackHost(BridgeRequest req)
    {
        if (!req.Headers.TryGetValue("Host", out var host) || host.Length == 0) return false;
        var colon = host.IndexOf(':');
        var hostName = colon < 0 ? host : host.Substring(0, colon);
        return string.Equals(hostName, "127.0.0.1", StringComparison.Ordinal);
    }

    private static string ReasonText(int status) => status switch
    {
        400 => "Bad Request",
        413 => "Payload Too Large",
        431 => "Request Header Fields Too Large",
        _ => "Error",
    };
}
