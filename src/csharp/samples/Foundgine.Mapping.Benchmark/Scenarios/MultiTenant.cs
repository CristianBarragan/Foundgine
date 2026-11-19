using Foundgine.Core.Mapping;
namespace Mapping.Benchmark.Scenarios;
public static class MultiTenant { public static MappingModel Build() =>
// BEGIN CONFIG
new MappingModel()
    .Map<Customer>(m => { m.Tenant(x => x.TenantId); m.Table("tbl_customer", "Id").Convention(); })
    .Map<CustomerSummary>(m => { m.Tenant(x => x.TenantId); m.Table("tbl_customer", "Id").Convention(); });
// END CONFIG
}
