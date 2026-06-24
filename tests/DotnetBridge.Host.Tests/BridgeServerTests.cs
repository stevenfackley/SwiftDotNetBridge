using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using DotnetBridge.Abstractions;
using DotnetBridge.Host;
using Xunit;

public class BridgeServerTests
{
    [Fact]
    public async Task Round_trips_a_user_route_over_loopback()
    {
        var routes = new RouteTable();
        routes.MapGet("/ping", (_, _) => Task.FromResult(BridgeResponse.Text("pong")));
        routes.MapPost("/echo", async (req, _) =>
            BridgeResponse.Text(System.Text.Encoding.UTF8.GetString(req.Body)));

        var server = new BridgeServer(routes);
        var port = server.Start();
        Assert.True(port > 0);
        try
        {
            using var http = new HttpClient();
            var pong = await http.GetStringAsync($"http://127.0.0.1:{port}/ping");
            Assert.Equal("pong", pong);

            var echo = await http.PostAsync($"http://127.0.0.1:{port}/echo",
                new StringContent("hi there"));
            Assert.Equal("hi there", await echo.Content.ReadAsStringAsync());

            var nf = await http.GetAsync($"http://127.0.0.1:{port}/nope");
            Assert.Equal(System.Net.HttpStatusCode.NotFound, nf.StatusCode);
        }
        finally { server.Stop(); }
    }

    [Fact]
    public void Start_is_idempotent_returns_same_port()
    {
        var server = new BridgeServer(new RouteTable());
        var p1 = server.Start();
        var p2 = server.Start();
        Assert.Equal(p1, p2);
        server.Stop();
    }
}
