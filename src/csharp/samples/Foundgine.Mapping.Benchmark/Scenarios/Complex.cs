using Foundgine.Core.Mapping;
namespace Mapping.Benchmark.Scenarios;
public static class Complex { public static MappingModel Build() =>
// BEGIN CONFIG
new MappingModel().Map<Order>(m =>
{
    m.Tenant(x => x.TenantId);
    m.Owner(x => x.CustomerId);
    m.Table("Orders", "Id").Convention();
    m.Child<OrderLine>(x => x.Lines, "OrderLines", "Id", "OrderId", t => t.Convention());
    m.Child<Payment>(x => x.Payments, "Payments", "Id", "OrderId", t => t.Convention());
});
// END CONFIG
}
