using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.Metadata;
using Foundgine.Generated;
using Foundgine.SupplyChain.Advanced.OpenIntent.Domain;

namespace Foundgine.SupplyChain.Advanced.OpenIntent;

/// <summary>
/// The open-intent semantic model is generated from the CLR source model and
/// then receives a small semantic overlay. No entity, field, relationship, or
/// physical column is reconstructed here.
/// </summary>
public static class OpenIntentSemanticModel
{
    /// <summary>
    /// Gets the immutable metadata generated from the CLR source model.
    /// </summary>
    public static IMetadataCatalog Metadata { get; } = GeneratedMetadata.Build();

    /// <summary>
    /// Gets the semantic model produced by applying the sample-specific
    /// semantic overlay to the generated metadata.
    /// </summary>
    public static SemanticModel Model { get; } =
        Metadata.FromMetadata()
            .Overlay(BuildOverlay())
            .Build();

    private static SemanticModel BuildOverlay()
    {
        var builder = new SemanticModelBuilder();

        AddEntityAliases<Customer>(builder, "Customer", "client", "buyer");
        AddEntityAliases<Order>(builder, "Order", "purchase", "customer order");
        AddEntityAliases<OrderItem>(builder, "OrderItem", "line item", "order line");
        AddEntityAliases<Product>(builder, "Product", "item", "catalog item");
        AddEntityAliases<Supplier>(builder, "Supplier", "vendor", "seller");
        AddEntityAliases<Category>(builder, "Category", "product category");
        AddEntityAliases<Inventory>(builder, "Inventory", "stock", "stock level");
        AddEntityAliases<Warehouse>(builder, "Warehouse", "site", "distribution center");
        AddEntityAliases<Shipment>(builder, "Shipment", "delivery", "dispatch");
        AddEntityAliases<Carrier>(builder, "Carrier", "shipper", "delivery carrier");
        AddEntityAliases<PurchaseOrder>(builder, "PurchaseOrder", "PO", "purchase order");

        return builder.Build();
    }

    private static void AddEntityAliases<T>(
        SemanticModelBuilder builder,
        string entityName,
        params string[] aliases)
    {
        var entity = Metadata.Entities.Single(e =>
            string.Equals(
                e.Name,
                entityName,
                StringComparison.OrdinalIgnoreCase));

        builder.Entity<T>(
            entity.EntityId,
            entity.Name,
            e =>
            {
                e.Aliases(aliases);
            });
    }
}