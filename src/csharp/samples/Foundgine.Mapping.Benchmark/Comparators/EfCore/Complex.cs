using Microsoft.EntityFrameworkCore;
namespace Mapping.Benchmark.Comparators.Complex;
// BEGIN ENTITIES
public class OrderRow { public int Id { get; set; } public string TenantId { get; set; } = ""; public int CustomerId { get; set; } public List<OrderLineRow> Lines { get; set; } = []; public List<PaymentRow> Payments { get; set; } = []; }
public class OrderLineRow { public int Id { get; set; } public int OrderId { get; set; } public OrderRow Order { get; set; } = null!; public string Sku { get; set; } = ""; public int Quantity { get; set; } }
public class PaymentRow { public int Id { get; set; } public int OrderId { get; set; } public OrderRow Order { get; set; } = null!; public decimal Amount { get; set; } }
// END ENTITIES
public class Db(DbContextOptions o) : ScenarioDb(o)
{
// BEGIN CONFIG
    public DbSet<OrderRow> Orders => Set<OrderRow>();
    public DbSet<OrderLineRow> OrderLines => Set<OrderLineRow>();
    public DbSet<PaymentRow> Payments => Set<PaymentRow>();
    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<OrderRow>(e => { e.ToTable("Orders"); e.HasQueryFilter(x => x.TenantId == TenantId && x.CustomerId == CustomerId); });
        mb.Entity<OrderLineRow>(e => { e.ToTable("OrderLines"); e.HasOne(x => x.Order).WithMany(o => o.Lines).HasForeignKey(x => x.OrderId); e.HasQueryFilter(x => x.Order.TenantId == TenantId && x.Order.CustomerId == CustomerId); });
        mb.Entity<PaymentRow>(e => { e.ToTable("Payments"); e.HasOne(x => x.Order).WithMany(o => o.Payments).HasForeignKey(x => x.OrderId); e.HasQueryFilter(x => x.Order.TenantId == TenantId && x.Order.CustomerId == CustomerId); });
    }
// END CONFIG
}
public static class Projections
{
// BEGIN MAPPING
    public static IQueryable<Order> ToDomain(this IQueryable<OrderRow> q) =>
        q.Select(o => new Order
        {
            Id = o.Id, TenantId = o.TenantId, CustomerId = o.CustomerId,
            Lines = o.Lines.Select(l => new OrderLine { Id = l.Id, Sku = l.Sku, Quantity = l.Quantity }).ToList(),
            Payments = o.Payments.Select(p => new Payment { Id = p.Id, Amount = p.Amount }).ToList()
        });
// END MAPPING
}
