using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Sample.CrudModule;

/// <summary>
/// Source-generated JSON context for the customer DTOs. Source generation is the only
/// AOT/trim-safe way to (de)serialize (reflection-based JSON is disabled under trimming),
/// and camelCase output keeps the JSON idiomatic for Swift consumption.
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(Customer))]
[JsonSerializable(typeof(IReadOnlyList<Customer>))]
[JsonSerializable(typeof(CustomerInput))]
[JsonSerializable(typeof(ApiError))]
public partial class CustomerJsonContext : JsonSerializerContext
{
}
