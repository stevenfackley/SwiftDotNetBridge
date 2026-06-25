using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using DotnetBridge.Abstractions;
using DotnetBridge.Host;
using Xunit;

public class ObservabilityTests
{
    [Fact]
    public async Task Server_echoes_request_id_for_correlation()
    {
        var routes = new RouteTable();
        routes.MapGet("/ping", (_, _) => Task.FromResult(BridgeResponse.Text("pong")));
        using var server = new BridgeServer(routes);
        var port = server.Start();

        using var http = new HttpClient();
        using var req = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{port}/ping");
        req.Headers.Add("X-Request-ID", "abc-123");
        using var resp = await http.SendAsync(req);

        Assert.True(resp.Headers.TryGetValues("X-Request-ID", out var vals));
        Assert.Equal("abc-123", vals!.Single());
    }

    [Fact]
    public async Task Server_omits_request_id_header_when_caller_sends_none()
    {
        var routes = new RouteTable();
        routes.MapGet("/ping", (_, _) => Task.FromResult(BridgeResponse.Text("pong")));
        using var server = new BridgeServer(routes);
        var port = server.Start();

        using var http = new HttpClient();
        using var resp = await http.GetAsync($"http://127.0.0.1:{port}/ping");

        Assert.False(resp.Headers.Contains("X-Request-ID"));
    }
}
