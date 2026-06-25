using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using DotnetBridge.Abstractions;
using DotnetBridge.Host;
using Xunit;

public class MemoryTests
{
    [Fact]
    public async Task Server_sheds_with_503_under_memory_pressure()
    {
        BridgeMemory.Degraded = true;
        try
        {
            var routes = new RouteTable();
            routes.MapGet("/ping", (_, _) => Task.FromResult(BridgeResponse.Text("pong")));
            using var server = new BridgeServer(routes);
            var port = server.Start();

            using var http = new HttpClient();
            using var resp = await http.GetAsync($"http://127.0.0.1:{port}/ping");

            Assert.Equal(HttpStatusCode.ServiceUnavailable, resp.StatusCode);
        }
        finally { BridgeMemory.Recover(); }
    }

    [Fact]
    public void OnMemoryPressure_degrades_and_recover_clears()
    {
        try
        {
            BridgeMemory.OnMemoryPressure();
            Assert.True(BridgeMemory.Degraded);
        }
        finally { BridgeMemory.Recover(); }

        Assert.False(BridgeMemory.Degraded);
    }
}
