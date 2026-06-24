using DotnetBridge.Abstractions;
using Xunit;

public class RouteTableTests
{
    [Fact]
    public void Match_extracts_route_param()
    {
        var t = new RouteTable();
        t.MapGet("/feature/run/{id}", (_, _) => System.Threading.Tasks.Task.FromResult(BridgeResponse.Text("ok")));

        var m = t.Match("GET", "/feature/run/abc");

        Assert.NotNull(m);
        Assert.Equal("abc", m!.Value.Values["id"]);
    }

    [Fact]
    public void Match_returns_null_on_method_mismatch()
    {
        var t = new RouteTable();
        t.MapPost("/x", (_, _) => System.Threading.Tasks.Task.FromResult(BridgeResponse.Text("ok")));
        Assert.Null(t.Match("GET", "/x"));
    }
}
