using Foundgine.Core.Mapping;
namespace Mapping.Benchmark.Scenarios;
public static class ManyToMany { public static MappingModel Build() =>
// BEGIN CONFIG
new MappingModel()
    .Map<Product>(m =>
    {
        m.Tenant(x => x.TenantId);
        m.Table("Products", "Id").Convention();
        m.ManyToMany<Category>(x => x.Categories, "ProductCategories", "ProductId", "CategoryId");
    })
    .Map<Category>(m => { m.Tenant(x => x.TenantId); m.Table("Categories", "Id").Convention(); });
// END CONFIG
}
