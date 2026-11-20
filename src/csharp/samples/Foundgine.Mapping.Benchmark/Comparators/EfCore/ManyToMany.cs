using Microsoft.EntityFrameworkCore;
namespace Mapping.Benchmark.Comparators.ManyToMany;
// BEGIN ENTITIES
public class CategoryRow { public int Id { get; set; } public string TenantId { get; set; } = ""; public string Name { get; set; } = ""; }
public class ProductRow { public int Id { get; set; } public string TenantId { get; set; } = ""; public string Name { get; set; } = ""; public List<CategoryRow> Categories { get; set; } = []; }
// END ENTITIES
public class Db(DbContextOptions o) : ScenarioDb(o)
{
// BEGIN CONFIG
    public DbSet<ProductRow> Products => Set<ProductRow>();
    public DbSet<CategoryRow> Categories => Set<CategoryRow>();
    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<ProductRow>(e =>
        {
            e.ToTable("Products");
            e.HasQueryFilter(x => x.TenantId == TenantId);
            e.HasMany(x => x.Categories).WithMany().UsingEntity("ProductCategories", l => l.HasOne(typeof(CategoryRow)).WithMany().HasForeignKey("CategoryId"), r => r.HasOne(typeof(ProductRow)).WithMany().HasForeignKey("ProductId"));
        });
        mb.Entity<CategoryRow>(e => { e.ToTable("Categories"); e.HasQueryFilter(x => x.TenantId == TenantId); });
    }
// END CONFIG
}
public static class Projections
{
// BEGIN MAPPING
    public static IQueryable<Product> ToDomain(this IQueryable<ProductRow> q) =>
        q.Select(p => new Product { Id = p.Id, TenantId = p.TenantId, Name = p.Name, Categories = p.Categories.Select(c => new Category { Id = c.Id, TenantId = c.TenantId, Name = c.Name }).ToList() });
// END MAPPING
}
