using Foundgine.Core.Mapping;
namespace Mapping.Benchmark.Scenarios;
public static class Legacy { public static MappingModel Build() =>
// BEGIN CONFIG
new MappingModel().Map<Customer>(m =>
{
    m.Tenant(x => x.TenantId);
    m.Computed(x => x.DisplayName);
    m.Table("tbl_cust", "cust_id")
        .Column(x => x.Id, "cust_id").Column(x => x.TenantId, "tnt").Column(x => x.Name, "cust_nm")
        .Column(x => x.Email, "email_addr").Column(x => x.Address.Street, "street").Column(x => x.Address.City, "city");
});
// END CONFIG
}
