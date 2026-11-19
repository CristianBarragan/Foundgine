using Foundgine.Core.Mapping;
namespace Mapping.Benchmark.Scenarios;
public static class InheritanceTpt { public static MappingModel Build() =>
// BEGIN CONFIG
new MappingModel()
    .Map<Vehicle>(m => { m.Tenant(x => x.TenantId); m.Table("Vehicles", "Id").Convention(); })
    .Map<Truck>(m => { m.Base<Vehicle>(); m.Table("Trucks", "Id", TableRole.Derived).Parent("Vehicles", "Id", "Id").Column(x => x.PayloadKg, "PayloadKg"); });
// END CONFIG
}
