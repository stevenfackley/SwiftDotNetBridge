namespace Sample.CrudModule;

/// <summary>A persisted customer record (mock domain entity).</summary>
public sealed class Customer
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string CreatedUtc { get; set; } = "";
}

/// <summary>Create/update payload — Id and CreatedUtc are server-assigned.</summary>
public sealed class CustomerInput
{
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
}
