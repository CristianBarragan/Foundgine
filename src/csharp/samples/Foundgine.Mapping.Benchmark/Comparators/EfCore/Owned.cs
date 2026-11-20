using Microsoft.EntityFrameworkCore;
namespace Mapping.Benchmark.Comparators.Owned;
// BEGIN ENTITIES
public class AddressRow { public string Street { get; set; } = ""; public string City { get; set; } = ""; }
public class CustomerRow { public int Id { get; set; } public string TenantId { get; set; } = ""; public string Name { get; set; } = ""; public string Email { get; set; } = ""; public string Bio { get; set; } = ""; public AddressRow Address { get; set; } = new(); }
// END ENTITIES
public class Db(DbContextOptions o) : ScenarioDb(o)
{
// BEGIN CONFIG
    public DbSet<CustomerRow> Customers => Set<CustomerRow>();
    protected override void OnModelCreating(ModelBuilder mb) =>
        mb.Entity<CustomerRow>(e =>
        {
            e.ToTable("Customer");
            e.HasQueryFilter(x => x.TenantId == TenantId);
            e.OwnsOne(x => x.Address, a => { a.ToTable("CustomerAddress"); a.WithOwner().HasForeignKey("CustomerId"); });
        });
// END CONFIG
}
public static class Projections
{
// BEGIN MAPPING
    public static IQueryable<Customer> ToDomain(this IQueryable<CustomerRow> q) =>
        q.Select(r => new Customer { Id = r.Id, TenantId = r.TenantId, Name = r.Name, Email = r.Email, Bio = r.Bio, Address = new Address(r.Address.Street, r.Address.City) });
// END MAPPING
}
