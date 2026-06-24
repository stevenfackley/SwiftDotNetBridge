using System.Collections.Generic;

namespace Sample.CrudModule;

/// <summary>
/// CRUD operations over customers. The in-memory implementation MOCKS what would
/// normally be a database/repository call — swap it for EF Core, Dapper, or an
/// HTTP client without touching the bridge or the Swift side.
/// </summary>
public interface ICustomerStore
{
    IReadOnlyList<Customer> List();
    Customer? Get(int id);
    Customer Create(CustomerInput input);
    Customer? Update(int id, CustomerInput input);
    bool Delete(int id);
}
