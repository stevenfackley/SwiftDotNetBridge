namespace Sample.CrudModule;

/// <summary>A persisted customer record (mock domain entity).</summary>
public sealed class Customer
{
    /// <summary>Server-assigned unique identifier.</summary>
    public int Id { get; set; }

    /// <summary>Customer display name.</summary>
    public string Name { get; set; } = "";

    /// <summary>Customer email address.</summary>
    public string Email { get; set; } = "";

    /// <summary>Creation timestamp, ISO-8601 round-trip ("o") format, UTC.</summary>
    public string CreatedUtc { get; set; } = "";
}

/// <summary>Create/update payload — Id and CreatedUtc are server-assigned.</summary>
public sealed class CustomerInput
{
    /// <summary>Customer display name (required, non-blank).</summary>
    public string Name { get; set; } = "";

    /// <summary>Customer email address (required; must contain "@").</summary>
    public string Email { get; set; } = "";
}

/// <summary>A structured error body so a JSON client gets JSON on failures, not plain text.</summary>
public sealed class ApiError
{
    /// <summary>Human-readable error message.</summary>
    public string Error { get; set; } = "";

    /// <summary>Parameterless constructor (required for JSON deserialization).</summary>
    public ApiError() { }

    /// <summary>Creates an error envelope carrying the given message.</summary>
    /// <param name="error">The human-readable error message.</param>
    public ApiError(string error) => Error = error;
}
