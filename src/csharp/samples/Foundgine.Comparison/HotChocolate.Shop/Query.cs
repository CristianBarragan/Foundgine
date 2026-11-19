using System.Security.Claims;
using HotChocolate.Authorization;
using HotChocolate.Data;
using HotChocolate.Shop.Data;

namespace HotChocolate.Shop;

public sealed class Query
{
    // Row-level rule: a customer only ever sees rows that belong to their own customer_id claim.
    private static int? CustomerId(ClaimsPrincipal user) =>
        user.IsInRole("customer") ? (int.TryParse(user.FindFirst("customer_id")?.Value, out var id) ? id : -1) : null;

    [GraphQLName("customer"), Authorize(Roles = new[] { "manager", "customer" }), UseFiltering, UseSorting]
    public IQueryable<Customer> GetCustomers(ShopDbContext db, ClaimsPrincipal user) =>
        (CustomerId(user) is { } id ? db.Customers.Where(c => c.Id == id) : db.Customers).AsNoTracking().ToModels();

    [GraphQLName("order"), Authorize(Roles = new[] { "manager", "customer" }), UseFiltering, UseSorting]
    public IQueryable<Order> GetOrders(ShopDbContext db, ClaimsPrincipal user) =>
        (CustomerId(user) is { } id ? db.Orders.Where(o => o.CustomerId == id) : db.Orders).AsNoTracking().ToModels();

    [GraphQLName("product"), Authorize(Roles = new[] { "manager", "customer" }), UseFiltering, UseSorting]
    public IQueryable<Product> GetProducts(ShopDbContext db) => db.Products.AsNoTracking().ToModels();

    [GraphQLName("orderLine"), Authorize(Roles = new[] { "manager" }), UseFiltering, UseSorting]
    public IQueryable<OrderLine> GetOrderLines(ShopDbContext db) => db.OrderLines.AsNoTracking().ToModels();
}
