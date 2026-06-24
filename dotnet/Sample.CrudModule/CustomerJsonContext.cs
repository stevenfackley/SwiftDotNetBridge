using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Sample.CrudModule;

// Source generation is the only AOT/trim-safe way to (de)serialize. camelCase
// JSON for idiomatic Swift consumption.
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(Customer))]
[JsonSerializable(typeof(IReadOnlyList<Customer>))]
[JsonSerializable(typeof(CustomerInput))]
public partial class CustomerJsonContext : JsonSerializerContext
{
}
