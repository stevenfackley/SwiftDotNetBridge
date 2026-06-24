using System;
using System.Collections.Generic;
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
    private readonly RouteTable _routes;
    private readonly object _gate = new();
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private int _port;

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
            _ = AcceptLoopAsync(listener, _cts.Token);
            return _port;
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            _cts?.Cancel();
            _listener?.Stop();
            _listener = null;
            _cts = null;
            _port = 0;
        }
    }

    public void Dispose()
    {
        Stop();
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                _ = HandleClientAsync(client, ct);
            }
        }
        catch (Exception) { /* listener stopped/cancelled */ }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        {
            try
            {
                var stream = client.GetStream();
                var req = await HttpRequestParser.ReadAsync(stream, ct).ConfigureAwait(false);
                if (req is null) return;

                BridgeResponse resp;
                var match = _routes.Match(req.Method, req.Path);
                if (match is null)
                {
                    resp = BridgeResponse.NotFound();
                }
                else
                {
                    var withValues = new BridgeRequest(req.Method, req.Path, req.RawQuery,
                        req.Headers, req.Body, match.Value.Values);
                    try { resp = await match.Value.Handler(withValues, ct).ConfigureAwait(false); }
                    catch (Exception ex)
                    {
                        resp = BridgeResponse.Text("Handler error: " + ex.Message, 500);
                    }
                }
                await HttpResponseWriter.WriteAsync(stream, resp, ct).ConfigureAwait(false);
            }
            catch (Exception) { /* per-connection failure: drop quietly */ }
        }
    }
}
