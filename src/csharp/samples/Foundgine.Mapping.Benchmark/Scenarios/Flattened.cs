using Foundgine.Core.Mapping;
namespace Mapping.Benchmark.Scenarios;
public static class Flattened { public static MappingModel Build() =>
// BEGIN CONFIG
new MappingModel().Map<Customer>(m =>
{
    m.Tenant(x => x.TenantId);
    m.Table("Customer", "Id").Convention()
        .Column(x => x.Address.Street, "Street").Column(x => x.Address.City, "City");
});
// END CONFIG
}
