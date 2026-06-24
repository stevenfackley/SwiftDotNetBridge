using System;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using DotnetBridge.Abstractions;

namespace Sample.CrudModule;

/// <summary>
/// A realistic REST/CRUD surface over ICustomerStore. This is the "whatever code
/// is in the .NET library" — add routes here; no ABI or Swift change needed.
/// </summary>
public sealed class CustomersModule : IBridgeModule
{
    private readonly ICustomerStore _store;

    public CustomersModule() : this(new InMemoryCustomerStore()) { }
    public CustomersModule(ICustomerStore store) => _store = store;

    public void Configure(RouteTable routes)
    {
        routes.MapGet("/health", (_, _) => Task.FromResult(BridgeResponse.Text("ok")));

        routes.MapGet("/api/customers", (_, _) =>
            Task.FromResult(BridgeResponse.Json(
                JsonSerializer.Serialize(_store.List(),
                    CustomerJsonContext.Default.IReadOnlyListCustomer))));

        routes.MapGet("/api/customers/{id}", (req, _) =>
        {
            if (!TryId(req, out var id)) return Task.FromResult(BridgeResponse.Text("Bad id", 400));
            var c = _store.Get(id);
            return Task.FromResult(c is null
                ? BridgeResponse.NotFound()
                : BridgeResponse.Json(JsonSerializer.Serialize(c, CustomerJsonContext.Default.Customer)));
        });

        routes.MapPost("/api/customers", (req, _) =>
        {
            var input = Parse(req.Body);
            if (input is null) return Task.FromResult(BridgeResponse.Text("Bad body", 400));
            var created = _store.Create(input);
            return Task.FromResult(BridgeResponse.Json(
                JsonSerializer.Serialize(created, CustomerJsonContext.Default.Customer), 201));
        });

        routes.MapPut("/api/customers/{id}", (req, _) =>
        {
            if (!TryId(req, out var id)) return Task.FromResult(BridgeResponse.Text("Bad id", 400));
            var input = Parse(req.Body);
            if (input is null) return Task.FromResult(BridgeResponse.Text("Bad body", 400));
            var updated = _store.Update(id, input);
            return Task.FromResult(updated is null
                ? BridgeResponse.NotFound()
                : BridgeResponse.Json(JsonSerializer.Serialize(updated, CustomerJsonContext.Default.Customer)));
        });

        routes.MapDelete("/api/customers/{id}", (req, _) =>
        {
            if (!TryId(req, out var id)) return Task.FromResult(BridgeResponse.Text("Bad id", 400));
            return Task.FromResult(_store.Delete(id)
                ? new BridgeResponse { StatusCode = 204 }
                : BridgeResponse.NotFound());
        });
    }

    private static bool TryId(BridgeRequest req, out int id) =>
        int.TryParse(req.RouteValues["id"], out id);

    private static CustomerInput? Parse(byte[] body)
    {
        if (body.Length == 0) return null;
        try
        {
            return JsonSerializer.Deserialize(
                Encoding.UTF8.GetString(body), CustomerJsonContext.Default.CustomerInput);
        }
        catch (JsonException) { return null; }
    }
}
