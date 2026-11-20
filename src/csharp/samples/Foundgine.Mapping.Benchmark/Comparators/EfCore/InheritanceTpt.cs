using Microsoft.EntityFrameworkCore;
namespace Mapping.Benchmark.Comparators.InheritanceTpt;
// BEGIN ENTITIES
public class VehicleRow { public int Id { get; set; } public string TenantId { get; set; } = ""; public string Plate { get; set; } = ""; }
public class TruckRow : VehicleRow { public decimal PayloadKg { get; set; } }
// END ENTITIES
public class Db(DbContextOptions o) : ScenarioDb(o)
{
// BEGIN CONFIG
    public DbSet<VehicleRow> Vehicles => Set<VehicleRow>();
    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<VehicleRow>(e => { e.UseTptMappingStrategy(); e.ToTable("Vehicles"); e.HasQueryFilter(x => x.TenantId == TenantId); });
        mb.Entity<TruckRow>().ToTable("Trucks");
    }
// END CONFIG
}
public static class Projections
{
// BEGIN MAPPING
    public static IQueryable<Truck> ToDomain(this IQueryable<TruckRow> q) =>
        q.Select(r => new Truck { Id = r.Id, TenantId = r.TenantId, Plate = r.Plate, PayloadKg = r.PayloadKg });
// END MAPPING
}
