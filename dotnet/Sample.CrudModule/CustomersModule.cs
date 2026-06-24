using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using DotnetBridge.Abstractions;

namespace Sample.CrudModule;

/// <summary>
/// A realistic REST/CRUD surface over ICustomerStore. This is the "whatever code
/// is in the .NET library" — add routes here; no ABI or Swift change needed.
/// Errors return a JSON envelope (<see cref="ApiError"/>) so a JSON client never
/// has to parse a plain-text body on the failure path.
/// </summary>
public sealed class CustomersModule : IBridgeModule
{
    private readonly ICustomerStore _store;

    /// <summary>Create the module over the default in-memory store.</summary>
    public CustomersModule() : this(new InMemoryCustomerStore()) { }

    /// <summary>Create the module over a specific store (e.g. EF Core/Dapper in production).</summary>
    public CustomersModule(ICustomerStore store) => _store = store;

    /// <inheritdoc />
    public void Configure(RouteTable routes)
    {
        routes.MapGet("/health", (_, _) => Task.FromResult(BridgeResponse.Text("ok")));

        routes.MapGet("/api/customers", (_, _) =>
            Task.FromResult(BridgeResponse.Json(
                JsonSerializer.Serialize(_store.List(),
                    CustomerJsonContext.Default.IReadOnlyListCustomer))));

        routes.MapGet("/api/customers/{id}", (req, _) =>
        {
            if (!TryId(req, out var id)) return Task.FromResult(Error("invalid id", 400));
            var c = _store.Get(id);
            return Task.FromResult(c is null
                ? NotFound()
                : BridgeResponse.Json(JsonSerializer.Serialize(c, CustomerJsonContext.Default.Customer)));
        });

        routes.MapPost("/api/customers", (req, _) =>
        {
            var input = Parse(req.Body);
            if (input is null) return Task.FromResult(Error("invalid JSON body", 400));
            var invalid = Validate(input);
            if (invalid is not null) return Task.FromResult(Error(invalid, 400));
            var created = _store.Create(input);
            return Task.FromResult(BridgeResponse.Json(
                JsonSerializer.Serialize(created, CustomerJsonContext.Default.Customer), 201));
        });

        routes.MapPut("/api/customers/{id}", (req, _) =>
        {
            if (!TryId(req, out var id)) return Task.FromResult(Error("invalid id", 400));
            var input = Parse(req.Body);
            if (input is null) return Task.FromResult(Error("invalid JSON body", 400));
            var invalid = Validate(input);
            if (invalid is not null) return Task.FromResult(Error(invalid, 400));
            var updated = _store.Update(id, input);
            return Task.FromResult(updated is null
                ? NotFound()
                : BridgeResponse.Json(JsonSerializer.Serialize(updated, CustomerJsonContext.Default.Customer)));
        });

        routes.MapDelete("/api/customers/{id}", (req, _) =>
        {
            if (!TryId(req, out var id)) return Task.FromResult(Error("invalid id", 400));
            return Task.FromResult(_store.Delete(id)
                ? new BridgeResponse { StatusCode = 204 }
                : NotFound());
        });
    }

    private static bool TryId(BridgeRequest req, out int id) =>
        int.TryParse(req.RouteValues["id"], out id);

    /// <summary>Returns an error message if the input is invalid, otherwise null.</summary>
    private static string? Validate(CustomerInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Name)) return "name is required";
        // Deliberately lenient: a real app would use a stricter validator. "@" must not be
        // first so there is at least a local part. IndexOf(char) is netstandard2.0-safe.
        if (string.IsNullOrWhiteSpace(input.Email) || input.Email.IndexOf('@') <= 0)
            return "a valid email is required";
        return null;
    }

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

    private static BridgeResponse Error(string message, int status) =>
        BridgeResponse.Json(
            JsonSerializer.Serialize(new ApiError(message), CustomerJsonContext.Default.ApiError), status);

    private static BridgeResponse NotFound() => Error("not found", 404);
}
