using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DotnetBridge.Abstractions;
using Sample.CrudModule;
using Xunit;

public class CustomersModuleTests
{
    private static BridgeRequest Req(RouteTable routes, string method, string path, string body = "")
    {
        var m = routes.Match(method, path);
        Assert.NotNull(m);
        return new BridgeRequest(method, path, "", new Dictionary<string, string>(),
            Encoding.UTF8.GetBytes(body), m!.Value.Values);
    }

    private static Task<BridgeResponse> Invoke(RouteTable routes, string method, string path, string body = "")
    {
        var m = routes.Match(method, path)!.Value;
        return m.Handler(Req(routes, method, path, body), CancellationToken.None);
    }

    [Fact]
    public async Task Lists_seeded_customers()
    {
        var routes = new RouteTable();
        new CustomersModule().Configure(routes);

        var resp = await Invoke(routes, "GET", "/api/customers");

        Assert.Equal(200, resp.StatusCode);
        Assert.Contains("Ada Lovelace", Encoding.UTF8.GetString(resp.Body));
    }

    [Fact]
    public async Task Creates_customer_returns_201()
    {
        var routes = new RouteTable();
        new CustomersModule(new InMemoryCustomerStore()).Configure(routes);

        var resp = await Invoke(routes, "POST", "/api/customers",
            "{\"name\":\"Nikola Tesla\",\"email\":\"nikola@example.com\"}");

        Assert.Equal(201, resp.StatusCode);
        Assert.Contains("Nikola Tesla", Encoding.UTF8.GetString(resp.Body));
    }

    [Fact]
    public async Task Get_unknown_returns_404()
    {
        var routes = new RouteTable();
        new CustomersModule().Configure(routes);

        var resp = await Invoke(routes, "GET", "/api/customers/9999");

        Assert.Equal(404, resp.StatusCode);
    }
}
