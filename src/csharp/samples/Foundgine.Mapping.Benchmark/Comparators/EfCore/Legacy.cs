using Microsoft.EntityFrameworkCore;
namespace Mapping.Benchmark.Comparators.Legacy;
// BEGIN ENTITIES
public class CustomerRow { public int Id { get; set; } public string TenantId { get; set; } = ""; public string Name { get; set; } = ""; public string Email { get; set; } = ""; public string Street { get; set; } = ""; public string City { get; set; } = ""; }
// END ENTITIES
public class Db(DbContextOptions o) : ScenarioDb(o)
{
// BEGIN CONFIG
    public DbSet<CustomerRow> Customers => Set<CustomerRow>();
    protected override void OnModelCreating(ModelBuilder mb) =>
        mb.Entity<CustomerRow>(e =>
        {
            e.ToTable("tbl_cust");
            e.Property(x => x.Id).HasColumnName("cust_id");
            e.Property(x => x.TenantId).HasColumnName("tnt");
            e.Property(x => x.Name).HasColumnName("cust_nm");
            e.Property(x => x.Email).HasColumnName("email_addr");
            e.Property(x => x.Street).HasColumnName("street");
            e.Property(x => x.City).HasColumnName("city");
            e.HasQueryFilter(x => x.TenantId == TenantId);
        });
// END CONFIG
}
public static class Projections
{
// BEGIN MAPPING
    public static IQueryable<Customer> ToDomain(this IQueryable<CustomerRow> q) =>
        q.Select(r => new Customer { Id = r.Id, TenantId = r.TenantId, Name = r.Name, Email = r.Email, Address = new Address(r.Street, r.City) });
// END MAPPING
}
