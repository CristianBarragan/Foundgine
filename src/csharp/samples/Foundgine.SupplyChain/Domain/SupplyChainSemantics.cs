using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Metadata;
using Foundgine.Providers.Aot;

namespace Foundgine.SupplyChain.Domain;

/// <summary>
/// Strongly typed application-facing handles (entity, field and relationship ids) for the
/// Supply Chain sample.
///
/// This replaces the <c>GeneratedSemanticModel</c> type that the AOT source generator used to
/// emit from <c>[FoundgineEntity]</c> / <c>[FoundgineModel]</c> attributes. The sample is now
/// configured fluently (<see cref="SupplyChainModel"/>), so the generator has no declarations to
/// work from and emits nothing. The ids are the same ones the fluent compiler assigns:
/// <list type="bullet">
/// <item><c>EntityId.Create(semanticEntityName)</c>, where the name is the one passed to
/// <c>ToEntity(...)</c> in <c>ModelConfigurations.cs</c>;</item>
/// <item><c>FieldId.Create(semanticEntityName, modelMemberName)</c>;</item>
/// <item><c>RelationshipId.Create(semanticEntityName, navigationMemberName)</c>.</item>
/// </list>
/// Every handle is checked against the compiled registry when its class is first used, so a
/// rename in the fluent configuration fails fast with a clear message instead of silently
/// producing an id the registry has never heard of.
/// </summary>
public static class SupplyChainSemantics
{
    private static readonly MetadataRegistry Registry = new SupplyChainModel().Build();

    private static EntityId EntityOf(string entityName)
    {
        var id = EntityId.Create(entityName);
        if (!Registry.TryGet(id, out _))
            throw new InvalidOperationException(
                $"Semantic entity '{entityName}' is not part of SupplyChainModel. " +
                "Update SupplyChainSemantics to match the ToEntity(...) names in ModelConfigurations.cs.");
        return id;
    }

    private static GeneratedSemanticField FieldOf(EntityId entity, string entityName, string member)
    {
        var id = FieldId.Create(entityName, member);
        if (!Registry.Get(entity).EffectiveFields.Any(f => f.Id == id))
            throw new InvalidOperationException(
                $"Field '{entityName}.{member}' is not mapped in SupplyChainModel. " +
                "Check the model member name and its entity map in Mappings.cs.");
        return new GeneratedSemanticField(entity, id, member);
    }

    private static RelationshipId RelationshipOf(string entityName, string member)
    {
        var id = RelationshipId.Create(entityName, member);
        if (!Registry.Relationships.Any(r => r.Id == id))
            throw new InvalidOperationException(
                $"Relationship '{entityName}.{member}' is not configured in SupplyChainModel. " +
                "Check HasRelationship(...) in ModelConfigurations.cs and the connection in the model.");
        return id;
    }

    private static IReadOnlyList<FieldId> AllFieldsOf(EntityId entity) =>
        Registry.Get(entity).EffectiveFields.Select(f => f.Id).ToArray();

    public static class Customer
    {
        private const string EntityName = "Customer";
        public static readonly EntityId Entity = EntityOf(EntityName);
        public static IReadOnlyList<FieldId> All { get; } = AllFieldsOf(Entity);
    }

    public static class SalesOrder
    {
        private const string EntityName = "SalesOrder";
        public static readonly EntityId Entity = EntityOf(EntityName);
        public static readonly GeneratedSemanticField Id = FieldOf(Entity, EntityName, "Id");
        public static readonly GeneratedSemanticField CustomerId = FieldOf(Entity, EntityName, "CustomerId");
        public static IReadOnlyList<FieldId> All { get; } = AllFieldsOf(Entity);

        public static class Relationships
        {
            public static readonly RelationshipId Lines = RelationshipOf(EntityName, "Lines");
        }
    }

    public static class SalesOrderLine
    {
        private const string EntityName = "SalesOrderLine";
        public static readonly EntityId Entity = EntityOf(EntityName);
        public static IReadOnlyList<FieldId> All { get; } = AllFieldsOf(Entity);
    }

    public static class CatalogProduct
    {
        private const string EntityName = "CatalogProduct";
        public static readonly EntityId Entity = EntityOf(EntityName);
        public static readonly GeneratedSemanticField Id = FieldOf(Entity, EntityName, "Id");
        public static IReadOnlyList<FieldId> All { get; } = AllFieldsOf(Entity);
    }

    public static class Supplier
    {
        private const string EntityName = "Supplier";
        public static readonly EntityId Entity = EntityOf(EntityName);
        public static IReadOnlyList<FieldId> All { get; } = AllFieldsOf(Entity);
    }

    public static class InventoryPosition
    {
        private const string EntityName = "InventoryPosition";
        public static readonly EntityId Entity = EntityOf(EntityName);
        public static readonly GeneratedSemanticField WarehouseId = FieldOf(Entity, EntityName, "WarehouseId");
        public static readonly GeneratedSemanticField ProductId = FieldOf(Entity, EntityName, "ProductId");
        public static readonly GeneratedSemanticField QuantityOnHand = FieldOf(Entity, EntityName, "QuantityOnHand");
        public static IReadOnlyList<FieldId> All { get; } = AllFieldsOf(Entity);
    }

    public static class Shipment
    {
        private const string EntityName = "Shipment";
        public static readonly EntityId Entity = EntityOf(EntityName);
        public static readonly GeneratedSemanticField Id = FieldOf(Entity, EntityName, "Id");
        public static readonly GeneratedSemanticField OrderId = FieldOf(Entity, EntityName, "OrderId");
        public static readonly GeneratedSemanticField CarrierId = FieldOf(Entity, EntityName, "CarrierId");
        public static readonly GeneratedSemanticField WarehouseId = FieldOf(Entity, EntityName, "WarehouseId");
        public static readonly GeneratedSemanticField TrackingNumber = FieldOf(Entity, EntityName, "TrackingNumber");
        public static readonly GeneratedSemanticField Status = FieldOf(Entity, EntityName, "Status");
        public static IReadOnlyList<FieldId> All { get; } = AllFieldsOf(Entity);

        public static class Relationships
        {
            public static readonly RelationshipId Order = RelationshipOf(EntityName, "Order");
        }
    }
}
