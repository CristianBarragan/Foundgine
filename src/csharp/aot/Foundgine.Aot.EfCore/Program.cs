using Foundgine.Core.Abstractions;
using Foundgine.Core.Execution;
using Foundgine.Core.Semantic.Metadata;
using Foundgine.Core.Semantic.Planning;
using Foundgine.Providers.Aot;
using Foundgine.Providers.EntityFrameworkCore;
using Foundgine.Providers.Orm;
using Foundgine.Generated.Orm;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using ExecutionContext = Foundgine.Core.Execution.ExecutionContext;

var entityId = new EntityId(901);
var idField = new FieldId(1);
var tenantField = new FieldId(2);
var nameField = new FieldId(3);
var metadata = new EntityMetadata(
    entityId,
    "AotCustomer",
    [],
    StorageName: "AotCustomers",
    Fields:
    [
        new FieldMetadata(idField, "Id", typeof(int), new ColumnReference(entityId, new ColumnId(1))),
        new FieldMetadata(tenantField, "TenantId", typeof(Guid), new ColumnReference(entityId, new ColumnId(2))),
        new FieldMetadata(nameField, "Name", typeof(string), new ColumnReference(entityId, new ColumnId(3)))
    ],
    PrimaryKey: new ColumnReference(entityId, new ColumnId(1)),
    ClrType: typeof(AotCustomer));

var registry = OrmRegistry.Create([metadata], GeneratedOrmMaps.CreateAll());
await using var sqliteConnection = new SqliteConnection("Data Source=:memory:");
await sqliteConnection.OpenAsync();
await using var db = new SmokeDbContext(sqliteConnection);
await db.Database.EnsureCreatedAsync();
db.Customers.AddRange(
    new AotCustomer { Id = 1, TenantId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"), Name = "Authorized" },
    new AotCustomer { Id = 2, TenantId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002"), Name = "Must not escape" });
await db.SaveChangesAsync();
db.ChangeTracker.Clear();

var tenantPredicate = AuthorizationPredicate.Equal(
    AuthorizationPredicate.Member(AuthorizationPredicate.ResourceParameter("resource"), "TenantId"),
    AuthorizationPredicate.Member(AuthorizationPredicate.ContextParameter("ctx"), "TenantId"));
var ir = new ExecutionIR(
    new ExecutionIRNode(1, ExecutionOperation.Scan, entityId, [idField, nameField], null, null, [], Authorization: tenantPredicate),
    [],
    new SemanticPlanAuthorizationBinding("aot-smoke-contract", "aot-smoke-authorization"));
var plan = new EfCorePlanCompiler(registry).Compile(ir);
var result = await new EfCoreExecutionProvider(db, registry).ExecuteAsync(
    plan,
    new ExecutionContext(new Dictionary<string, object?>
    {
        ["ctx.TenantId"] = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001")
    }));

if (result.Rows.Count != 1 || !Equals(result.Rows[0].Values["Name"], "Authorized"))
    throw new InvalidOperationException("Foundgine EF Core Native AOT authorization/projection pipeline failed.");
if (result.Rows[0].Values.ContainsKey("TenantId"))
    throw new InvalidOperationException("Foundgine exposed an internal authorization field in the result.");
if (db.ChangeTracker.Entries().Any())
    throw new InvalidOperationException("Foundgine EF Core query unexpectedly tracked entities.");

Console.WriteLine($"Foundgine EF Core Native AOT pipeline passed ({FoundgineEfCore.ProviderName}): tenant isolation + generated map + projection.");

public sealed class SmokeDbContext(SqliteConnection connection) : DbContext
{
    public DbSet<AotCustomer> Customers => Set<AotCustomer>();

    protected override void OnConfiguring(DbContextOptionsBuilder options) =>
        options.UseSqlite(connection);

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<AotCustomer>(entity =>
        {
            entity.ToTable("AotCustomers");
            entity.HasKey(customer => customer.Id);
            entity.Property(customer => customer.Id).ValueGeneratedNever();
        });
}

[FoundgineEntity("AotCustomer", Id = 901, StorageName = "AotCustomers")]
public sealed class AotCustomer
{
    [FoundgineField("Id", Id = 1, ColumnId = 1, IsPrimaryKey = true)]
    public int Id { get; set; }

    [FoundgineField("TenantId", Id = 2, ColumnId = 2)]
    public Guid TenantId { get; set; }

    [FoundgineField("Name", Id = 3, ColumnId = 3)]
    public string Name { get; set; } = string.Empty;
}
