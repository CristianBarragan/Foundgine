using Foundgine.Core.Semantic.Configuration;
using Foundgine.Providers.EntityFrameworkCore;

namespace Foundgine.Shop.Domain;

/// <summary>The whole Foundgine declaration: semantics, storage, mapping, relationships. Build() validates it at startup.</summary>
public sealed class ShopModel : FoundgineModel
{
    public override void Configure(FoundgineModelBuilder model)
    {
        // Semantic: relationships between models
        model.Configure<CustomerSemantics>();
        model.Configure<OrderSemantics>();
        model.Configure<OrderLineSemantics>();
        model.Configure<ProductSemantics>();
        // Persistence: table, key, column names
        model.Configure<CustomerTable>();
        model.Configure<ProductTable>();
        model.Configure<OrderTable>();
        model.Configure<OrderLineTable>();
        // Model member -> storage member (same class here)
        model.Configure<IdentityEntityMap<Customer>>();
        model.Configure<IdentityEntityMap<Product>>();
        model.Configure<IdentityEntityMap<Order>>();
        model.Configure<IdentityEntityMap<OrderLine>>();
        // Relationship -> foreign key
        model.Configure<ShopConnections>();
    }
}

internal sealed class CustomerSemantics : FoundgineModelConfiguration<Customer>
{ public override void Configure(FoundgineModelBuilder<Customer> m) => m.HasRelationship(x => x.Orders).To<Order>(); }

internal sealed class OrderSemantics : FoundgineModelConfiguration<Order>
{
    public override void Configure(FoundgineModelBuilder<Order> m)
    { m.HasRelationship(x => x.Customer).To<Customer>(); m.HasRelationship(x => x.Lines).To<OrderLine>(); }
}

internal sealed class OrderLineSemantics : FoundgineModelConfiguration<OrderLine>
{ public override void Configure(FoundgineModelBuilder<OrderLine> m) => m.HasRelationship(x => x.Product).To<Product>(); }

internal sealed class ProductSemantics : FoundgineModelConfiguration<Product>
{ public override void Configure(FoundgineModelBuilder<Product> m) { } }

internal sealed class CustomerTable : FoundginePersistenceConfiguration<Customer>
{
    public override void Configure(FoundginePersistenceBuilder<Customer> m)
    {
        m.ToTable("tbl_customer").HasKey(x => x.Id);
        m.Property(x => x.Id).HasColumnName("cust_id");
        m.Property(x => x.FirstName).HasColumnName("first_nm");
        m.Property(x => x.LastName).HasColumnName("last_nm");
        m.Property(x => x.Email).HasColumnName("email_addr");
    }
}

internal sealed class ProductTable : FoundginePersistenceConfiguration<Product>
{
    public override void Configure(FoundginePersistenceBuilder<Product> m)
    {
        m.ToTable("tbl_product").HasKey(x => x.Id);
        m.Property(x => x.Id).HasColumnName("prod_id");
        m.Property(x => x.Sku).HasColumnName("prod_sku");
        m.Property(x => x.Name).HasColumnName("prod_title");
        m.Property(x => x.Price).HasColumnName("list_price");
    }
}

internal sealed class OrderTable : FoundginePersistenceConfiguration<Order>
{
    public override void Configure(FoundginePersistenceBuilder<Order> m)
    {
        m.ToTable("tbl_order").HasKey(x => x.Id);
        m.Property(x => x.Id).HasColumnName("ord_id");
        m.Property(x => x.CustomerId).HasColumnName("cust_id");
        m.Property(x => x.Status).HasColumnName("ord_status");
        m.Property(x => x.Total).HasColumnName("ord_total");
    }
}

internal sealed class OrderLineTable : FoundginePersistenceConfiguration<OrderLine>
{
    public override void Configure(FoundginePersistenceBuilder<OrderLine> m)
    {
        m.ToTable("tbl_order_line").HasKey(x => x.Id);
        m.Property(x => x.Id).HasColumnName("line_id");
        m.Property(x => x.OrderId).HasColumnName("ord_id");
        m.Property(x => x.ProductId).HasColumnName("prod_id");
        m.Property(x => x.Quantity).HasColumnName("qty");
        m.Property(x => x.UnitPrice).HasColumnName("unit_price");
    }
}

internal sealed class ShopConnections : FoundgineConnectionConfiguration
{
    public override void Configure(FoundgineConnectionBuilder c)
    {
        // Customer 1 - * Order (also defines Order.Customer)
        c.HasRelationship<Customer, Order>().Using<Customer, Order>().HasForeignKey(o => o.CustomerId).References(x => x.Id);
        // Order 1 - * OrderLine
        c.HasRelationship<Order, OrderLine>().Using<Order, OrderLine>().HasForeignKey(l => l.OrderId).References(o => o.Id);
        // OrderLine * - 1 Product
        c.HasRelationship<OrderLine, Product>().Using<OrderLine, Product>().HasForeignKeyOnSource(l => l.ProductId).References(p => p.Id);
    }
}
