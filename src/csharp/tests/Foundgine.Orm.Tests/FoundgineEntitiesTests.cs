using System.Text.Json;
using Foundgine.Core.Semantic.Metadata;
using Foundgine.Generated.Orm;
using Foundgine.Providers.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static Foundgine.Orm.Tests.OrmTestModel;

namespace Foundgine.Orm.Tests;

/// <summary>
/// The generic entity gateway over EF Core + SQLite, with metadata that carries no CLR type (the generator shape):
/// types come from <c>MapClrTypes</c>, maps from <c>AddMaps</c>. Writes only; the semantic read path needs the full
/// planner pipeline and is covered by the sample's E2E run.
/// </summary>
public sealed class FoundgineEntitiesTests
{
    private static FoundgineEfModel Model(bool withClrTypes = true)
    {
        var metadata = new MetadataRegistry();
        foreach (var entity in AllMetadata()) metadata.Register(entity);
        foreach (var relationship in Relationships()) metadata.Register(relationship);

        var options = new FoundgineEfOptions().AddMaps(GeneratedOrmMaps.CreateAll());
        if (withClrTypes)
            options.MapClrTypes(new Dictionary<string, Type>
            {
                ["OrmCustomer"] = typeof(OrmCustomer), ["OrmOrder"] = typeof(OrmOrder),
                ["OrmTag"] = typeof(OrmTag), ["OrmLine"] = typeof(OrmLine)
            });
        return new FoundgineEfModel(metadata, options);
    }

    private static async Task<(SqliteConnection Connection, FoundgineDbContext Db, FoundgineEfModel Model)> OpenAsync()
    {
        var model = Model();
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var db = new FoundgineDbContext(
            new DbContextOptionsBuilder<FoundgineDbContext>().UseSqlite(connection).Options, model);
        await db.Database.EnsureCreatedAsync();
        return (connection, db, model);
    }

    private static FoundgineEntity<T> Gateway<T>(FoundgineDbContext db, FoundgineEfModel model) where T : class =>
        new(model.Entities.Single(e => e.ClrType == typeof(T)), new FoundgineEfRepository<T>(db), null!);

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void MapClrTypes_resolves_the_clr_type_of_metadata_that_carries_none()
    {
        Assert.All(AllMetadata(), e => Assert.Null(e.ClrType));
        var model = Model();
        Assert.Equal(typeof(OrmOrder), model.Entities.Single(e => e.Name == "OrmOrder").ClrType);
        Assert.Equal(4, model.Entities.Count(e => e.ClrType is not null));
    }

    [Fact]
    public void Entities_without_a_resolved_clr_type_have_no_ef_mapping()
    {
        // No MapClrTypes and no CLR type on the metadata: the generated maps still bind, nothing is mapped to EF.
        Assert.All(Model(withClrTypes: false).Entities, e => Assert.Null(e.ClrType));
    }

    [Fact]
    public async Task Create_get_update_delete_roundtrip_by_json()
    {
        var (connection, db, model) = await OpenAsync();
        await using var _ = connection;
        await using var __ = db;
        var orders = Gateway<OrmOrder>(db, model);

        var created = (OrmOrder)await orders.CreateAsync(
            Json("""{ "id": 7, "customerId": 1, "sku": "A", "total": 10.5, "placedOn": "2026-01-02T03:04:05" }"""));
        Assert.Equal(7, created.Id);

        var loaded = (OrmOrder)(await orders.GetAsync(Json("7")))!; // key arrives as a JSON number
        Assert.Equal("A", loaded.Sku);

        var updated = await orders.UpdateAsync(7, new Dictionary<string, object?> { ["total"] = Json("12.25") });
        Assert.Equal(1, updated);
        Assert.Equal(12.25m, ((OrmOrder)(await orders.GetAsync(7))!).Total);

        Assert.Equal(1, await orders.DeleteAsync(7));
        Assert.Null(await orders.GetAsync(7));
        Assert.Equal(0, await orders.DeleteAsync(7));
    }

    [Fact]
    public async Task Integer_ids_are_coerced_to_a_long_key()
    {
        var (connection, db, model) = await OpenAsync();
        await using var _ = connection;
        await using var __ = db;
        var tags = Gateway<OrmTag>(db, model);

        await tags.CreateAsync(Json("""{ "id": 3, "customerId": 1, "label": "vip" }"""));
        var tag = (OrmTag)(await tags.GetAsync(3))!; // int -> long
        Assert.Equal("vip", tag.Label);
    }

    [Fact]
    public async Task Unknown_fields_and_key_updates_are_rejected()
    {
        var (connection, db, model) = await OpenAsync();
        await using var _ = connection;
        await using var __ = db;
        var orders = Gateway<OrmOrder>(db, model);
        await orders.CreateAsync(Json("""{ "id": 1, "customerId": 1, "sku": "A", "total": 1, "placedOn": "2026-01-01T00:00:00" }"""));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            orders.CreateAsync(Json("""{ "id": 2, "nope": 1 }""")));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            orders.UpdateAsync(1, new Dictionary<string, object?> { ["nope"] = 1 }));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            orders.UpdateAsync(1, new Dictionary<string, object?> { ["Id"] = 99 }));
    }
}
