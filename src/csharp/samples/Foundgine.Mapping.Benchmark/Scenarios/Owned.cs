using Foundgine.Core.Mapping;
namespace Mapping.Benchmark.Scenarios;
public static class Owned { public static MappingModel Build() =>
// BEGIN CONFIG
new MappingModel().Map<Customer>(m =>
{
    m.Tenant(x => x.TenantId);
    m.Table("Customer", "Id").Convention();
    m.Child<Address>(x => x.Address, "CustomerAddress", "CustomerId", "CustomerId", t => t.Convention());
});
// END CONFIG
}
