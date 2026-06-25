using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using DotnetBridge.Abstractions;
using DotnetBridge.Host;
using Xunit;

public class LifecycleTests
{
    [Fact]
    public async Task Stop_lets_in_flight_request_finish()
    {
        var entered = new SemaphoreSlim(0);
        var routes = new RouteTable();
        routes.MapGet("/slow", async (_, ct) =>
        {
            entered.Release();
            await Task.Delay(150, ct);          // work that fits inside the 2s graceful window
            return BridgeResponse.Text("done");
        });
        var server = new BridgeServer(routes);
        var port = server.Start();

        using var http = new HttpClient();
        var pending = http.GetStringAsync($"http://127.0.0.1:{port}/slow");
        await entered.WaitAsync();              // handler is mid-flight

        server.Stop();                          // graceful: should let it finish, not abort it

        Assert.Equal("done", await pending);
    }

    [Fact]
    public void Stop_is_idempotent_and_safe_when_not_running()
    {
        var server = new BridgeServer(new RouteTable());
        server.Stop();                          // never started — must be a no-op
        var p = server.Start();
        Assert.True(p > 0);
        server.Stop();
        server.Stop();                          // double stop — must not throw
    }
}
