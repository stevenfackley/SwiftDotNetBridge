using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DotnetBridge.Abstractions;
using DotnetBridge.Host;
using Xunit;

public class AuthAndCanonTests
{
    private static Task<BridgeRequest?> Parse(string raw) =>
        HttpRequestParser.ReadAsync(new MemoryStream(Encoding.ASCII.GetBytes(raw)),
            maxLineBytes: 8192, maxHeaderCount: 100, maxBodyBytes: 1024, CancellationToken.None);

    // ---------- path / method canonicalization (parser) ----------

    [Fact]
    public async Task Parser_rejects_encoded_separator()
    {
        var ex = await Assert.ThrowsAsync<BridgeProtocolException>(() => Parse("GET /api%2fx HTTP/1.1\r\n\r\n"));
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task Parser_rejects_dot_segments()
    {
        var ex = await Assert.ThrowsAsync<BridgeProtocolException>(() => Parse("GET /a/../b HTTP/1.1\r\n\r\n"));
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task Parser_rejects_double_slash()
    {
        var ex = await Assert.ThrowsAsync<BridgeProtocolException>(() => Parse("GET /a//b HTTP/1.1\r\n\r\n"));
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task Parser_rejects_connect()
    {
        var ex = await Assert.ThrowsAsync<BridgeProtocolException>(() => Parse("CONNECT example.com:443 HTTP/1.1\r\n\r\n"));
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task Parser_rejects_absolute_form()
    {
        var ex = await Assert.ThrowsAsync<BridgeProtocolException>(() => Parse("GET http://127.0.0.1/x HTTP/1.1\r\n\r\n"));
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task Parser_accepts_normal_path()
    {
        var req = await Parse("GET /api/customers/5?q=1 HTTP/1.1\r\nHost: 127.0.0.1\r\n\r\n");
        Assert.Equal("/api/customers/5", req!.Path);
        Assert.Equal("q=1", req.RawQuery);
    }

    // ---------- host binding (server) ----------

    [Fact]
    public async Task Server_rejects_non_loopback_host()
    {
        var routes = new RouteTable();
        routes.MapGet("/ping", (_, _) => Task.FromResult(BridgeResponse.Text("pong")));
        using var server = new BridgeServer(routes);
        var port = server.Start();

        using var http = new HttpClient();
        using var req = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{port}/ping");
        req.Headers.Host = "evil.example.com";   // spoofed Host on a loopback connection
        using var resp = await http.SendAsync(req);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    // ---------- capability-token auth (server, global) ----------

    [Fact]
    public async Task Server_enforces_token_when_configured()
    {
        BridgeAuth.Configure("s3cr3t-capability-token");
        try
        {
            var routes = new RouteTable();
            routes.MapGet("/ping", (_, _) => Task.FromResult(BridgeResponse.Text("pong")));
            using var server = new BridgeServer(routes);
            var port = server.Start();
            using var http = new HttpClient();

            // missing token -> 401
            using (var r = await http.GetAsync($"http://127.0.0.1:{port}/ping"))
                Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);

            // wrong token -> 401
            using (var bad = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{port}/ping"))
            {
                bad.Headers.Add("X-DNI-Auth", "not-the-token");
                using var r = await http.SendAsync(bad);
                Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
            }

            // correct token -> 200
            using (var ok = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{port}/ping"))
            {
                ok.Headers.Add("X-DNI-Auth", "s3cr3t-capability-token");
                using var r = await http.SendAsync(ok);
                Assert.Equal(HttpStatusCode.OK, r.StatusCode);
                Assert.Equal("pong", await r.Content.ReadAsStringAsync());
            }
        }
        finally { BridgeAuth.Configure(null); }
    }

    [Fact]
    public void Auth_disabled_by_default_authorizes_anything()
    {
        Assert.True(BridgeAuth.IsAuthorized(null));
        Assert.False(BridgeAuth.IsEnabled);
    }
}
