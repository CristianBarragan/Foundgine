using Foundgine.Core.Mapping;
namespace Mapping.Benchmark.Scenarios;
public static class InheritanceTph { public static MappingModel Build() =>
// BEGIN CONFIG
new MappingModel()
    .Map<Vehicle>(m => { m.Tenant(x => x.TenantId); m.Table("Vehicles", "Id").Convention(); })
    .Map<Car>(m => { m.Base<Vehicle>(); m.Table("Vehicles", "Id", TableRole.Derived).Column(x => x.Doors, "Doors").Discriminator("Kind", "car"); });
// END CONFIG
}
