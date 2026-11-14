using Foundgine.Providers.EntityFrameworkCore;
using Foundgine.SupplyChain.Domain.Models;

namespace Foundgine.SupplyChain.Infrastructure;

/// <summary>
/// Demo rows written with <see cref="IFoundgineRepository{T}"/> (the same CRUD the endpoints use). Runs only on an
/// empty database; identities are generated in insertion order, so alice is customer 1 and bob customer 2.
/// </summary>
public static class DemoData
{
    public static async Task SeedAsync(IServiceProvider sp, CancellationToken ct)
    {
        var customers = sp.GetRequiredService<IFoundgineRepository<Customer>>();
        if (await customers.CountAsync(null, ct) > 0) return;

        await customers.AddAsync(new Customer { FirstName = "Alice", LastName = "Anders", Email = "alice@example.com" }, ct);
        await customers.AddAsync(new Customer { FirstName = "Bob", LastName = "Brandt", Email = "bob@example.com" }, ct);

        var supplier = await sp.GetRequiredService<IFoundgineRepository<Supplier>>()
            .AddAsync(new Supplier { Name = "Acme Components", Email = "sales@acme.example" }, ct);
        var category = await sp.GetRequiredService<IFoundgineRepository<Category>>()
            .AddAsync(new Category { Name = "Hardware" }, ct);
        var products = sp.GetRequiredService<IFoundgineRepository<CatalogProduct>>();
        var bearing = await products.AddAsync(new CatalogProduct
            { Name = "Bearing", Sku = "SKU-100", UnitPrice = 12.50m, SupplierId = supplier.Id, CategoryId = category.Id }, ct);
        var gasket = await products.AddAsync(new CatalogProduct
            { Name = "Gasket", Sku = "SKU-200", UnitPrice = 3.25m, SupplierId = supplier.Id, CategoryId = category.Id }, ct);

        var warehouses = sp.GetRequiredService<IFoundgineRepository<Warehouse>>();
        var austin = await warehouses.AddAsync(new Warehouse { Name = "Austin DC", Location = "Austin, TX" }, ct);
        var rotterdam = await warehouses.AddAsync(new Warehouse { Name = "Rotterdam DC", Location = "Rotterdam, NL" }, ct);
        await sp.GetRequiredService<IFoundgineRepository<Carrier>>().AddAsync(new Carrier { Name = "FastFreight" }, ct);

        var inventory = sp.GetRequiredService<IFoundgineRepository<InventoryPosition>>();
        foreach (var (w, p, qty) in new[] { (austin.Id, bearing.Id, 100), (rotterdam.Id, bearing.Id, 40), (austin.Id, gasket.Id, 500) })
            await inventory.AddAsync(new InventoryPosition { WarehouseId = w, ProductId = p, QuantityOnHand = qty, ReorderLevel = 20 }, ct);
    }
}
