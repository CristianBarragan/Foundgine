using HotChocolate.Shop.Data;

namespace HotChocolate.Shop;

// Entity -> model projections. EF cannot translate recursive projections, so each root has its own acyclic shape:
// Order roots carry Customer (without Orders) and Lines -> Product; Customer roots carry Orders -> Lines -> Product.
public static class Mappings
{
    public static IQueryable<Product> ToModels(this IQueryable<ProductRow> q) =>
        q.Select(p => new Product { Id = p.Id, Sku = p.Sku, Name = p.Title, Price = p.ListPrice });

    public static IQueryable<Order> ToModels(this IQueryable<OrderRow> q) =>
        q.Select(o => new Order
        {
            Id = o.Id, CustomerId = o.CustomerId, Status = o.Status, Total = o.Total,
            Customer = new Customer { Id = o.Customer.Id, FirstName = o.Customer.FirstName, LastName = o.Customer.LastName, Email = o.Customer.Email },
            Lines = o.Lines.Select(l => new OrderLine
            {
                Id = l.Id, OrderId = l.OrderId, ProductId = l.ProductId, Quantity = l.Qty, UnitPrice = l.UnitPrice,
                Product = new Product { Id = l.Product.Id, Sku = l.Product.Sku, Name = l.Product.Title, Price = l.Product.ListPrice }
            }).ToList()
        });

    public static IQueryable<OrderLine> ToModels(this IQueryable<OrderLineRow> q) =>
        q.Select(l => new OrderLine
        {
            Id = l.Id, OrderId = l.OrderId, ProductId = l.ProductId, Quantity = l.Qty, UnitPrice = l.UnitPrice,
            Product = new Product { Id = l.Product.Id, Sku = l.Product.Sku, Name = l.Product.Title, Price = l.Product.ListPrice }
        });

    public static IQueryable<Customer> ToModels(this IQueryable<CustomerRow> q) =>
        q.Select(c => new Customer
        {
            Id = c.Id, FirstName = c.FirstName, LastName = c.LastName, Email = c.Email,
            Orders = c.Orders.Select(o => new Order
            {
                Id = o.Id, CustomerId = o.CustomerId, Status = o.Status, Total = o.Total,
                Lines = o.Lines.Select(l => new OrderLine
                {
                    Id = l.Id, OrderId = l.OrderId, ProductId = l.ProductId, Quantity = l.Qty, UnitPrice = l.UnitPrice,
                    Product = new Product { Id = l.Product.Id, Sku = l.Product.Sku, Name = l.Product.Title, Price = l.Product.ListPrice }
                }).ToList()
            }).ToList()
        });
}
