using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DotnetBridge.Abstractions;
using DotnetBridge.Host;
using Xunit;

public class HardeningTests
{
    // ---------- parser size limits (Findings B, C) — pure, no globals ----------

    [Fact]
    public async Task Parser_rejects_oversized_body_with_413()
    {
        var raw = "POST /x HTTP/1.1\r\nContent-Length: 50\r\n\r\n" + new string('a', 50);
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes(raw));

        var ex = await Assert.ThrowsAsync<BridgeProtocolException>(() =>
            HttpRequestParser.ReadAsync(stream, maxLineBytes: 8192, maxHeaderCount: 100,
                maxBodyBytes: 10, CancellationToken.None));

        Assert.Equal(413, ex.StatusCode);
    }

    [Fact]
    public async Task Parser_rejects_oversized_line_with_431()
    {
        var raw = "GET /" + new string('a', 100) + " HTTP/1.1\r\n\r\n";
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes(raw));

        var ex = await Assert.ThrowsAsync<BridgeProtocolException>(() =>
            HttpRequestParser.ReadAsync(stream, maxLineBytes: 16, maxHeaderCount: 100,
                maxBodyBytes: 1000, CancellationToken.None));

        Assert.Equal(431, ex.StatusCode);
    }

    [Fact]
    public async Task Parser_rejects_too_many_headers_with_431()
    {
        var sb = new StringBuilder("GET /x HTTP/1.1\r\n");
        for (var i = 0; i < 10; i++) sb.Append('H').Append(i).Append(": v\r\n");
        sb.Append("\r\n");
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes(sb.ToString()));

        var ex = await Assert.ThrowsAsync<BridgeProtocolException>(() =>
            HttpRequestParser.ReadAsync(stream, maxLineBytes: 8192, maxHeaderCount: 3,
                maxBodyBytes: 1000, CancellationToken.None));

        Assert.Equal(431, ex.StatusCode);
    }

    // ---------- server behavior over loopback ----------

    [Fact]
    public async Task Server_returns_500_generic_on_handler_throw_without_leaking()
    {
        var routes = new RouteTable();
        routes.MapGet("/boom", (_, _) => throw new InvalidOperationException("SECRET-detail-xyz"));
        using var server = new BridgeServer(routes);
        var port = server.Start();

        using var http = new HttpClient();
        using var resp = await http.GetAsync($"http://127.0.0.1:{port}/boom");
        var body = await resp.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, resp.StatusCode);
        Assert.Equal("Internal Server Error", body);
        Assert.DoesNotContain("SECRET", body);   // Finding G: no internal detail leaks to the client
    }

    [Fact]
    public async Task Server_returns_405_for_known_path_wrong_method()
    {
        var routes = new RouteTable();
        routes.MapGet("/only", (_, _) => Task.FromResult(BridgeResponse.Text("ok")));
        using var server = new BridgeServer(routes);
        var port = server.Start();

        using var http = new HttpClient();
        using var resp = await http.PostAsync($"http://127.0.0.1:{port}/only", new StringContent(""));

        Assert.Equal(HttpStatusCode.MethodNotAllowed, resp.StatusCode);   // Finding K
    }

    [Fact]
    public async Task Server_returns_404_for_unknown_path()
    {
        var routes = new RouteTable();
        routes.MapGet("/only", (_, _) => Task.FromResult(BridgeResponse.Text("ok")));
        using var server = new BridgeServer(routes);
        var port = server.Start();

        using var http = new HttpClient();
        using var resp = await http.GetAsync($"http://127.0.0.1:{port}/missing");

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Server_returns_413_for_oversized_body()
    {
        var original = BridgeLimits.MaxBodyBytes;
        BridgeLimits.MaxBodyBytes = 8;
        try
        {
            var routes = new RouteTable();
            routes.MapPost("/x", (req, _) => Task.FromResult(BridgeResponse.Text("got " + req.Body.Length)));
            using var server = new BridgeServer(routes);
            var port = server.Start();

            using var http = new HttpClient();
            using var resp = await http.PostAsync($"http://127.0.0.1:{port}/x",
                new StringContent("this body is far too large for the limit"));

            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, resp.StatusCode);   // 413
        }
        finally { BridgeLimits.MaxBodyBytes = original; }
    }

    [Fact]
    public async Task Diagnostics_hook_receives_handler_error()
    {
        string? ctx = null;
        Exception? err = null;
        var cls = BridgeErrorClass.Unknown;
        BridgeDiagnostics.OnError = (k, c, e) => { cls = k; ctx = c; err = e; };
        try
        {
            var routes = new RouteTable();
            routes.MapGet("/boom", (_, _) => throw new InvalidOperationException("kaboom"));
            using var server = new BridgeServer(routes);
            var port = server.Start();

            using var http = new HttpClient();
            using var _ = await http.GetAsync($"http://127.0.0.1:{port}/boom");

            Assert.NotNull(ctx);
            Assert.Contains("handler error", ctx!);                 // Finding F: the failure is observable
            Assert.IsType<InvalidOperationException>(err);
            Assert.Equal(BridgeErrorClass.Handler, cls);            // #5: classified failure
        }
        finally { BridgeDiagnostics.OnError = null; }
    }

    // ---------- router 404/405 helper (Finding K) ----------

    [Fact]
    public void RouteTable_IsKnownPath_matches_param_path_any_method()
    {
        var t = new RouteTable();
        t.MapGet("/api/items/{id}", (_, _) => Task.FromResult(BridgeResponse.Text("ok")));

        Assert.True(t.IsKnownPath("/api/items/42"));
        Assert.False(t.IsKnownPath("/api/other"));
    }

    // ---------- status constants stay in sync with dni.h (Finding H) ----------

    [Fact]
    public void DniStatus_values_match_abi_header()
    {
        Assert.Equal(0, DniStatus.Ok);
        Assert.Equal(-1, DniStatus.NotInitialized);
        Assert.Equal(-2, DniStatus.InvalidArgument);
        Assert.Equal(-4, DniStatus.AlreadyRunning);
        Assert.Equal(-5, DniStatus.Internal);
    }
}
