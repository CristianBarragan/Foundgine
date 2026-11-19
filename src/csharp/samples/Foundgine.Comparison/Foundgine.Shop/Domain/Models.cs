namespace Foundgine.Shop.Domain;

// API models. Foundgine's EF provider uses the model class as the EF entity (see README); the database shape
// (tbl_*, cust_id, qty, ...) is declared separately in ShopModel.cs. Navigation members only name relationships.
public sealed class Customer { public int Id { get; init; } public string FirstName { get; init; } = ""; public string LastName { get; init; } = ""; public string Email { get; init; } = ""; public IReadOnlyList<Order> Orders { get; init; } = new List<Order>(); }
public sealed class Product { public int Id { get; init; } public string Sku { get; init; } = ""; public string Name { get; init; } = ""; public decimal Price { get; init; } }
public sealed class Order { public int Id { get; init; } public int CustomerId { get; init; } public string Status { get; init; } = ""; public decimal Total { get; init; } public Customer Customer { get; init; } = null!; public IReadOnlyList<OrderLine> Lines { get; init; } = new List<OrderLine>(); }
public sealed class OrderLine { public int Id { get; init; } public int OrderId { get; init; } public int ProductId { get; init; } public int Quantity { get; init; } public decimal UnitPrice { get; init; } public Product Product { get; init; } = null!; }
