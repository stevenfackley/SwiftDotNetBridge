using System.Collections.Generic;

namespace Sample.CrudModule;

/// <summary>
/// CRUD operations over customers. The in-memory implementation MOCKS what would
/// normally be a database/repository call — swap it for EF Core, Dapper, or an
/// HTTP client without touching the bridge or the Swift side.
/// </summary>
public interface ICustomerStore
{
    /// <summary>Returns all customers.</summary>
    IReadOnlyList<Customer> List();

    /// <summary>Returns the customer with the given id, or <see langword="null"/> if none exists.</summary>
    /// <param name="id">The customer id to look up.</param>
    Customer? Get(int id);

    /// <summary>Creates a new customer from the input and returns the persisted record.</summary>
    /// <param name="input">The create payload (name/email).</param>
    Customer Create(CustomerInput input);

    /// <summary>
    /// Updates the customer with the given id and returns the updated record,
    /// or <see langword="null"/> if no such customer exists.
    /// </summary>
    /// <param name="id">The customer id to update.</param>
    /// <param name="input">The new field values.</param>
    Customer? Update(int id, CustomerInput input);

    /// <summary>Deletes the customer with the given id; returns <see langword="true"/> if one was removed.</summary>
    /// <param name="id">The customer id to delete.</param>
    bool Delete(int id);
}
