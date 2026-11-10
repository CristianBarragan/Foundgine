using Foundgine.Core.Abstractions;
using Foundgine.Core.Execution;
using Foundgine.Core.Semantic.Metadata;
using Foundgine.Core.Semantic.Planning;
using Foundgine.Core.Semantic.Query;
using Foundgine.Providers.Aot;
using ExecutionContext = Foundgine.Core.Execution.ExecutionContext;

namespace Foundgine.Orm.Tests;

[FoundgineEntity(Id = 401, StorageName = "OrmCustomers")]
public sealed class OrmCustomer
{
    [FoundgineField(Id = 1, StorageName = "Id", IsPrimaryKey = true)]
    public int Id { get; set; }

    [FoundgineField(Id = 2, StorageName = "TenantId")]
    public Guid TenantId { get; set; }

    [FoundgineField(Id = 3, StorageName = "Name")]
    public string Name { get; set; } = string.Empty;

    [FoundgineField(Id = 4, StorageName = "Email")]
    public string? Email { get; set; }

    [FoundgineField(Id = 5, StorageName = "IsActive")]
    public bool IsActive { get; set; }

    [FoundgineRelationship(typeof(OrmOrder), "CustomerId", "Id", Id = 1, Name = "Orders")]
    public List<OrmOrder> Orders { get; set; } = [];
}

[FoundgineEntity(Id = 402, StorageName = "OrmOrders")]
public sealed class OrmOrder
{
    [FoundgineField(Id = 11, StorageName = "Id", IsPrimaryKey = true)]
    public int Id { get; set; }

    [FoundgineField(Id = 12, StorageName = "CustomerId")]
    public int CustomerId { get; set; }

    [FoundgineField(Id = 13, StorageName = "Sku")]
    public string Sku { get; set; } = string.Empty;

    [FoundgineField(Id = 14, StorageName = "Total")]
    public decimal Total { get; set; }

    [FoundgineField(Id = 15, StorageName = "PlacedOn")]
    public DateTime PlacedOn { get; set; }
}

[FoundgineEntity(Id = 403, StorageName = "OrmTags")]
public sealed class OrmTag
{
    [FoundgineField(Id = 21, StorageName = "Id", IsPrimaryKey = true)]
    public long Id { get; set; }

    [FoundgineField(Id = 22, StorageName = "CustomerId")]
    public long CustomerId { get; set; }

    [FoundgineField(Id = 23, StorageName = "Label")]
    public string Label { get; set; } = string.Empty;
}

[FoundgineEntity(Id = 404, StorageName = "OrmLines")]
public sealed class OrmLine
{
    [FoundgineField(Id = 31, StorageName = "Id", IsPrimaryKey = true)]
    public int Id { get; set; }

    [FoundgineField(Id = 32, StorageName = "OrderId")]
    public int OrderId { get; set; }

    [FoundgineField(Id = 33, StorageName = "Quantity")]
    public int Quantity { get; set; }
}

internal static class OrmTestModel
{
    public static readonly Guid TenantA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    public static readonly Guid TenantB = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
    public static readonly EntityId Customer = new(401);
    public static readonly FieldId Id = new(1), Tenant = new(2), Name = new(3), Email = new(4), Active = new(5);

    public static readonly EntityId Order = new(402), Tag = new(403), Line = new(404);
    public static readonly FieldId OrderId = new(11), OrderCustomerId = new(12), Sku = new(13), Total = new(14), PlacedOn = new(15);
    public static readonly FieldId TagId = new(21), TagCustomerId = new(22), Label = new(23);
    public static readonly FieldId LineId = new(31), LineOrderId = new(32), Quantity = new(33);
    public static readonly RelationshipId CustomerOrders = new(1), CustomerTags = new(2), OrderLines = new(3);

    private static FieldMetadata F(EntityId e, FieldId id, string name, Type type) =>
        new(id, name, type, new ColumnReference(e, new ColumnId(id.Value)));

    private static EntityMetadata Entity(EntityId id, string name, string storage, FieldId pk, params FieldMetadata[] fields) =>
        new(id, name, [], StorageName: storage, Fields: fields, PrimaryKey: new ColumnReference(id, new ColumnId(pk.Value)));

    /// <summary>Hand-built metadata equal to what the existing generator emits for the test entities.</summary>
    public static EntityMetadata Metadata() => Entity(Customer, "OrmCustomer", "OrmCustomers", Id,
        F(Customer, Id, "Id", typeof(int)), F(Customer, Tenant, "TenantId", typeof(Guid)),
        F(Customer, Name, "Name", typeof(string)), F(Customer, Email, "Email", typeof(string)),
        F(Customer, Active, "IsActive", typeof(bool)));

    public static IReadOnlyList<EntityMetadata> AllMetadata() =>
    [
        Metadata(),
        Entity(Order, "OrmOrder", "OrmOrders", OrderId,
            F(Order, OrderId, "Id", typeof(int)), F(Order, OrderCustomerId, "CustomerId", typeof(int)),
            F(Order, Sku, "Sku", typeof(string)), F(Order, Total, "Total", typeof(decimal)),
            F(Order, PlacedOn, "PlacedOn", typeof(DateTime))),
        Entity(Tag, "OrmTag", "OrmTags", TagId,
            F(Tag, TagId, "Id", typeof(long)), F(Tag, TagCustomerId, "CustomerId", typeof(long)),
            F(Tag, Label, "Label", typeof(string))),
        Entity(Line, "OrmLine", "OrmLines", LineId,
            F(Line, LineId, "Id", typeof(int)), F(Line, LineOrderId, "OrderId", typeof(int)),
            F(Line, Quantity, "Quantity", typeof(int)))
    ];

    /// <summary>Customer 1:N Order and 1:N Tag (long keys: exercises int/long key matching), Order 1:N Line.</summary>
    public static IReadOnlyList<RelationshipMetadata> Relationships() =>
    [
        new(CustomerOrders, Customer, Order, "Orders", new ColumnReference(Customer, new ColumnId(Id.Value)),
            new ColumnReference(Order, new ColumnId(OrderCustomerId.Value))),
        new(CustomerTags, Customer, Tag, "Tags", new ColumnReference(Customer, new ColumnId(Id.Value)),
            new ColumnReference(Tag, new ColumnId(TagCustomerId.Value))),
        new(OrderLines, Order, Line, "Lines", new ColumnReference(Order, new ColumnId(OrderId.Value)),
            new ColumnReference(Line, new ColumnId(LineOrderId.Value)))
    ];

    public static object[] SeedAll() => [.. Seed(), .. SeedOrders(), .. SeedTags(), .. SeedLines()];

    public static OrmOrder[] SeedOrders() =>
    [
        new() { Id = 1, CustomerId = 1, Sku = "A", Total = 10m, PlacedOn = new DateTime(2026, 1, 1) },
        new() { Id = 2, CustomerId = 1, Sku = "B", Total = 20m, PlacedOn = new DateTime(2026, 1, 2) },
        new() { Id = 3, CustomerId = 2, Sku = "C", Total = 30m, PlacedOn = new DateTime(2026, 1, 3) },
        new() { Id = 4, CustomerId = 4, Sku = "D", Total = 40m, PlacedOn = new DateTime(2026, 1, 4) }
    ];

    public static OrmTag[] SeedTags() =>
    [
        new() { Id = 1, CustomerId = 1, Label = "vip" },
        new() { Id = 2, CustomerId = 1, Label = "early" },
        new() { Id = 3, CustomerId = 3, Label = "lapsed" }
    ];

    public static OrmLine[] SeedLines() =>
    [
        new() { Id = 1, OrderId = 1, Quantity = 5 },
        new() { Id = 2, OrderId = 1, Quantity = 6 },
        new() { Id = 3, OrderId = 3, Quantity = 7 }
    ];

    public static OrmCustomer[] Seed() =>
    [
        new() { Id = 1, TenantId = TenantA, Name = "Ada", Email = "ada@a.test", IsActive = true },
        new() { Id = 2, TenantId = TenantA, Name = "Grace", Email = "grace@a.test", IsActive = true },
        new() { Id = 3, TenantId = TenantA, Name = "Linus", Email = null, IsActive = false },
        new() { Id = 4, TenantId = TenantB, Name = "Eve", Email = "eve@b.test", IsActive = true }
    ];

    /// <summary>tenant predicate: resource.TenantId == ctx.TenantId</summary>
    public static AuthorizationPredicate TenantPredicate() =>
        AuthorizationPredicate.Equal(
            AuthorizationPredicate.Member(AuthorizationPredicate.ResourceParameter("resource"), "TenantId"),
            AuthorizationPredicate.Member(AuthorizationPredicate.ContextParameter("ctx"), "TenantId"));

    public static ExecutionContext Context(Guid tenant) =>
        new(new Dictionary<string, object?> { ["ctx.TenantId"] = tenant });

    public static ExecutionIRNode Child(
        int id, EntityId entity, RelationshipId via, IReadOnlyList<FieldId> fields,
        IReadOnlyList<ExecutionIRNode>? children = null, SemanticQueryOptions? options = null,
        AuthorizationPredicate? authorization = null) =>
        new(id, ExecutionOperation.Traverse, entity, fields, via, null, children ?? [], options, authorization);

    public static ExecutionIR Ir(
        SemanticQueryOptions? options = null,
        AuthorizationPredicate? authorization = null,
        IReadOnlyList<FieldId>? fields = null,
        IReadOnlyList<ExecutionIRNode>? children = null) =>
        new(
            new ExecutionIRNode(
                1, ExecutionOperation.Scan, Customer, fields ?? [Id, Name], null, null,
                children ?? [], options, authorization ?? TenantPredicate()),
            [],
            new SemanticPlanAuthorizationBinding("test-contract", "test-authorization"));
}
