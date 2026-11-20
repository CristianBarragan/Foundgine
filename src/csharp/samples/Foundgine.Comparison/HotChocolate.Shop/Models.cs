namespace HotChocolate.Shop;

// GraphQL / API models: what callers see. Different names and shape from the EF entities.
public sealed class Customer { public int Id { get; set; } public string FirstName { get; set; } = ""; public string LastName { get; set; } = ""; public string Email { get; set; } = ""; public List<Order> Orders { get; set; } = []; }
public sealed class Product { public int Id { get; set; } public string Sku { get; set; } = ""; public string Name { get; set; } = ""; public decimal Price { get; set; } }
public sealed class Order { public int Id { get; set; } public int CustomerId { get; set; } public string Status { get; set; } = ""; public decimal Total { get; set; } public Customer? Customer { get; set; } public List<OrderLine> Lines { get; set; } = []; }
public sealed class OrderLine { public int Id { get; set; } public int OrderId { get; set; } public int ProductId { get; set; } public int Quantity { get; set; } public decimal UnitPrice { get; set; } public Product? Product { get; set; } }
