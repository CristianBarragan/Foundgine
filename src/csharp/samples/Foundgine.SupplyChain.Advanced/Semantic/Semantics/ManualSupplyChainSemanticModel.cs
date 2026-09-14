using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic;
using D = Foundgine.SupplyChain.Advanced.Domain;

namespace Foundgine.SupplyChain.Advanced.Semantics;

/// <summary>
/// Small hand-authored semantic overlay used by the running Supply Chain sample.
///
/// The complete structural graph is still discovered from AOT metadata by
/// <see cref="SupplyChainSemanticModel"/>. This class intentionally declares
/// only two entities so the sample demonstrates the typed semantic API without
/// recreating the entire supply-chain schema a second time.
///
/// The two entities are composed onto the metadata-discovered graph at startup.
/// The overlay adds application meaning (aliases, constraints, capabilities and
/// strongly typed relationships) while the metadata graph remains the source of
/// structural truth.
/// </summary>
public static class ManualSupplyChainSemanticModel
{
    public static readonly EntityId Product = EntityId.Create("Product");
    public static readonly EntityId ProductComponent = EntityId.Create("ProductComponent");

    public static SemanticModel Model { get; } = Build();

    public static SemanticModel Build()
    {
        var builder = new SemanticModelBuilder()
            // Only Product is manually authored here. The complete Product
            // schema in the running application still comes from metadata.
            .Entity<D.Product>(Product, "Product", e => e
                .Aliases("Item", "Item2")
                .Identity(x => x.Id)
                .Field(x => x.Sku)
                .FieldAlias(x => x.Sku, "PartNumber")
                .Constraint(x => x.Sku, SemanticConstraint.Pattern("^[A-Z0-9-]{3,32}$"))
                .Field(x => x.Name)
                .Field(x => x.Category,
                    capabilities: SemanticFieldCapabilities.Filterable |
                                  SemanticFieldCapabilities.Sortable |
                                  SemanticFieldCapabilities.Selectable)
                .Field(x => x.SafetyStock,
                    capabilities: SemanticFieldCapabilities.Default |
                                  SemanticFieldCapabilities.Writable)
                .Constraint(x => x.SafetyStock, SemanticConstraint.Range(minimum: 0m))
                .Relationship(
                    "components",
                    x => x.Id,
                    (D.ProductComponent x) => x.ParentProductId,
                    ProductComponent,
                    RelationshipCardinality.Many))

            // One related entity is enough to demonstrate strongly typed
            // relationship authoring and relationship aliases without
            // manually describing PurchaseOrders, Suppliers, Shipments, etc.
            .Entity<D.ProductComponent>(ProductComponent, "ProductComponent", e => e
                .Identity(x => x.ParentProductId)
                .Field(x => x.ComponentProductId)
                .Field(x => x.QuantityPerParent,
                    capabilities: SemanticFieldCapabilities.Default |
                                  SemanticFieldCapabilities.Writable)
                .Constraint(x => x.QuantityPerParent, SemanticConstraint.Range(minimum: 0m))
                .Relationship(
                    "componentProduct",
                    x => x.ComponentProductId,
                    (D.Product x) => x.Id,
                    Product,
                    RelationshipCardinality.One));

        return builder.Build();
    }

    public static FieldId Field(string entityName, string fieldName) =>
        Model.Entities.Single(entity =>
                string.Equals(entity.Name, entityName, StringComparison.OrdinalIgnoreCase))
            .Fields.Single(field =>
                string.Equals(field.Name, fieldName, StringComparison.OrdinalIgnoreCase)).Id;

    public static RelationshipId Relationship(string entityName, string relationshipName) =>
        Model.Entities.Single(entity =>
                string.Equals(entity.Name, entityName, StringComparison.OrdinalIgnoreCase))
            .Relationships.Single(relationship =>
                string.Equals(relationship.Name, relationshipName, StringComparison.OrdinalIgnoreCase)).Id;
}