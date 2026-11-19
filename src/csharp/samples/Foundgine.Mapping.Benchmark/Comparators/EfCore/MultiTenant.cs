using Microsoft.EntityFrameworkCore;
namespace Mapping.Benchmark.Comparators.MultiTenant;
// BEGIN ENTITIES
public class CustomerRow { public int Id { get; set; } public string TenantId { get; set; } = ""; public string Name { get; set; } = ""; public string Email { get; set; } = ""; public string Bio { get; set; } = ""; }
// END ENTITIES
public class Db(DbContextOptions o) : ScenarioDb(o)
{
// BEGIN CONFIG
    public DbSet<CustomerRow> Customers => Set<CustomerRow>();
    protected override void OnModelCreating(ModelBuilder mb) =>
        mb.Entity<CustomerRow>(e => { e.ToTable("tbl_customer"); e.HasQueryFilter(x => x.TenantId == TenantId); });
// END CONFIG
}
public static class Projections
{
// BEGIN MAPPING
    public static IQueryable<Customer> ToDomain(this IQueryable<CustomerRow> q) =>
        q.Select(r => new Customer { Id = r.Id, TenantId = r.TenantId, Name = r.Name, Email = r.Email, Bio = r.Bio });
    public static IQueryable<CustomerSummary> ToSummary(this IQueryable<CustomerRow> q) =>
        q.Select(r => new CustomerSummary { Id = r.Id, TenantId = r.TenantId, Name = r.Name });
// END MAPPING
}
