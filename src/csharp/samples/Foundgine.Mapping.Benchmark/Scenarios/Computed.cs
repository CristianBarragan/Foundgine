using Foundgine.Core.Mapping;
namespace Mapping.Benchmark.Scenarios;
public static class Computed { public static MappingModel Build() =>
// BEGIN CONFIG
new MappingModel().Map<Customer>(m =>
{
    m.Tenant(x => x.TenantId);
    m.Computed(x => x.DisplayName);
    m.Table("Customer", "Id").Convention();
});
// END CONFIG
}
