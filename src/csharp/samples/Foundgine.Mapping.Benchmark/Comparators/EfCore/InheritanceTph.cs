using Microsoft.EntityFrameworkCore;
namespace Mapping.Benchmark.Comparators.InheritanceTph;
// BEGIN ENTITIES
public class VehicleRow { public int Id { get; set; } public string TenantId { get; set; } = ""; public string Plate { get; set; } = ""; }
public class CarRow : VehicleRow { public int Doors { get; set; } }
// END ENTITIES
public class Db(DbContextOptions o) : ScenarioDb(o)
{
// BEGIN CONFIG
    public DbSet<VehicleRow> Vehicles => Set<VehicleRow>();
    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<VehicleRow>(e => { e.ToTable("Vehicles"); e.HasDiscriminator<string>("Kind").HasValue<CarRow>("car"); e.HasQueryFilter(x => x.TenantId == TenantId); });
    }
// END CONFIG
}
public static class Projections
{
// BEGIN MAPPING
    public static IQueryable<Car> ToDomain(this IQueryable<CarRow> q) =>
        q.Select(r => new Car { Id = r.Id, TenantId = r.TenantId, Plate = r.Plate, Doors = r.Doors });
// END MAPPING
}
