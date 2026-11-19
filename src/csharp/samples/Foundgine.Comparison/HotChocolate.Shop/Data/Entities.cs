using Microsoft.EntityFrameworkCore;

namespace HotChocolate.Shop.Data;

// EF entities: the database shape. Never exposed through GraphQL.
public sealed class CustomerRow { public int Id { get; set; } public string FirstName { get; set; } = ""; public string LastName { get; set; } = ""; public string Email { get; set; } = ""; public List<OrderRow> Orders { get; set; } = []; }
public sealed class ProductRow { public int Id { get; set; } public string Sku { get; set; } = ""; public string Title { get; set; } = ""; public decimal ListPrice { get; set; } }
public sealed class OrderRow { public int Id { get; set; } public int CustomerId { get; set; } public string Status { get; set; } = ""; public decimal Total { get; set; } public CustomerRow Customer { get; set; } = null!; public List<OrderLineRow> Lines { get; set; } = []; }
public sealed class OrderLineRow { public int Id { get; set; } public int OrderId { get; set; } public int ProductId { get; set; } public int Qty { get; set; } public decimal UnitPrice { get; set; } public ProductRow Product { get; set; } = null!; }

public sealed class ShopDbContext(DbContextOptions<ShopDbContext> options) : DbContext(options)
{
    public DbSet<CustomerRow> Customers => Set<CustomerRow>();
    public DbSet<ProductRow> Products => Set<ProductRow>();
    public DbSet<OrderRow> Orders => Set<OrderRow>();
    public DbSet<OrderLineRow> OrderLines => Set<OrderLineRow>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<CustomerRow>(e => { e.ToTable("tbl_customer"); e.HasKey(x => x.Id); e.Property(x => x.Id).HasColumnName("cust_id"); e.Property(x => x.FirstName).HasColumnName("first_nm"); e.Property(x => x.LastName).HasColumnName("last_nm"); e.Property(x => x.Email).HasColumnName("email_addr"); });
        mb.Entity<ProductRow>(e => { e.ToTable("tbl_product"); e.HasKey(x => x.Id); e.Property(x => x.Id).HasColumnName("prod_id"); e.Property(x => x.Sku).HasColumnName("prod_sku"); e.Property(x => x.Title).HasColumnName("prod_title"); e.Property(x => x.ListPrice).HasColumnName("list_price"); });
        mb.Entity<OrderRow>(e => { e.ToTable("tbl_order"); e.HasKey(x => x.Id); e.Property(x => x.Id).HasColumnName("ord_id"); e.Property(x => x.CustomerId).HasColumnName("cust_id"); e.Property(x => x.Status).HasColumnName("ord_status"); e.Property(x => x.Total).HasColumnName("ord_total"); e.HasOne(x => x.Customer).WithMany(c => c.Orders).HasForeignKey(x => x.CustomerId); });
        mb.Entity<OrderLineRow>(e => { e.ToTable("tbl_order_line"); e.HasKey(x => x.Id); e.Property(x => x.Id).HasColumnName("line_id"); e.Property(x => x.OrderId).HasColumnName("ord_id"); e.Property(x => x.ProductId).HasColumnName("prod_id"); e.Property(x => x.Qty).HasColumnName("qty"); e.Property(x => x.UnitPrice).HasColumnName("unit_price"); e.HasOne<OrderRow>().WithMany(o => o.Lines).HasForeignKey(x => x.OrderId); e.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId); });
    }
}
