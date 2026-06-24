using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;

namespace Sample.CrudModule;

/// <summary>Thread-safe in-memory mock store, seeded with sample data.</summary>
public sealed class InMemoryCustomerStore : ICustomerStore
{
    private readonly object _gate = new();
    private readonly Dictionary<int, Customer> _items = new();
    private int _nextId;

    public InMemoryCustomerStore()
    {
        // Mocked seed data — stands in for rows a real DB would return.
        Seed("Ada Lovelace", "ada@example.com");
        Seed("Alan Turing", "alan@example.com");
        Seed("Grace Hopper", "grace@example.com");
    }

    private void Seed(string name, string email)
    {
        var id = Interlocked.Increment(ref _nextId);
        _items[id] = new Customer
        {
            Id = id, Name = name, Email = email,
            CreatedUtc = "2026-01-01T00:00:00.0000000Z",
        };
    }

    public IReadOnlyList<Customer> List()
    {
        lock (_gate) return _items.Values.OrderBy(c => c.Id).ToList();
    }

    public Customer? Get(int id)
    {
        lock (_gate) return _items.TryGetValue(id, out var c) ? c : null;
    }

    public Customer Create(CustomerInput input)
    {
        lock (_gate)
        {
            var id = Interlocked.Increment(ref _nextId);
            var c = new Customer
            {
                Id = id, Name = input.Name, Email = input.Email,
                CreatedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
            };
            _items[id] = c;
            return c;
        }
    }

    public Customer? Update(int id, CustomerInput input)
    {
        lock (_gate)
        {
            if (!_items.TryGetValue(id, out var c)) return null;
            c.Name = input.Name;
            c.Email = input.Email;
            return c;
        }
    }

    public bool Delete(int id)
    {
        lock (_gate) return _items.Remove(id);
    }
}
