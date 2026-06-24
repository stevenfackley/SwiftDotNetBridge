using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DotnetBridge.Abstractions;
using Sample.CrudModule;
using Xunit;

public class CustomersValidationTests
{
    private static async Task<BridgeResponse> Post(string body)
    {
        var routes = new RouteTable();
        new CustomersModule(new InMemoryCustomerStore()).Configure(routes);
        var m = routes.Match("POST", "/api/customers")!.Value;
        var req = new BridgeRequest("POST", "/api/customers", "", new Dictionary<string, string>(),
            Encoding.UTF8.GetBytes(body), m.Values);
        return await m.Handler(req, CancellationToken.None);
    }

    [Fact]
    public async Task Blank_name_returns_400_json()
    {
        var resp = await Post("{\"name\":\"\",\"email\":\"a@b.com\"}");

        Assert.Equal(400, resp.StatusCode);
        Assert.Contains("application/json", resp.ContentType);          // Finding I: JSON, not text
        Assert.Contains("name is required", Encoding.UTF8.GetString(resp.Body));
    }

    [Fact]
    public async Task Bad_email_returns_400()
    {
        var resp = await Post("{\"name\":\"X\",\"email\":\"nope\"}");

        Assert.Equal(400, resp.StatusCode);
        Assert.Contains("valid email", Encoding.UTF8.GetString(resp.Body));
    }

    [Fact]
    public async Task Invalid_json_returns_400()
    {
        var resp = await Post("not json at all");

        Assert.Equal(400, resp.StatusCode);
        Assert.Contains("invalid JSON", Encoding.UTF8.GetString(resp.Body));
    }

    [Fact]
    public async Task Valid_input_still_creates_201()
    {
        var resp = await Post("{\"name\":\"Nikola Tesla\",\"email\":\"nikola@example.com\"}");

        Assert.Equal(201, resp.StatusCode);
        Assert.Contains("Nikola Tesla", Encoding.UTF8.GetString(resp.Body));
    }
}
