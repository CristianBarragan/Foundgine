using System.Linq.Expressions;

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
public static IMetadataCatalog Metadata { get; } = GeneratedMetadata.Build();


public static SemanticModel Model { get; } =
    Metadata.FromMetadata()
        .Overlay(BuildOverlay())
        .Build();

private static SemanticModel BuildOverlay()
{
    var builder = new SemanticModelBuilder();

    AddEntityAliases<Customer>(builder, "Customer", x => x.Id, "client", "buyer");
    AddEntityAliases<Order>(builder, "Order", x => x.Id, "purchase", "customer order");
    AddEntityAliases<OrderItem>(builder, "OrderItem", x => x.Id, "line item", "order line");
    AddEntityAliases<Product>(builder, "Product", x => x.Id, "item", "catalog item");
    AddEntityAliases<Supplier>(builder, "Supplier", x => x.Id, "vendor", "seller");
    AddEntityAliases<Category>(builder, "Category", x => x.Id, "product category");
    AddEntityAliases<Inventory>(builder, "Inventory", x => x.Id, "stock", "stock level");
    AddEntityAliases<Warehouse>(builder, "Warehouse", x => x.Id, "site", "distribution center");
    AddEntityAliases<Shipment>(builder, "Shipment", x => x.Id, "delivery", "dispatch");
    AddEntityAliases<Carrier>(builder, "Carrier", x => x.Id, "shipper", "delivery carrier");
    AddEntityAliases<PurchaseOrder>(builder, "PurchaseOrder", x => x.Id, "PO", "purchase order");

    return builder.Build();
}

private static void AddEntityAliases<T>(
    SemanticModelBuilder builder,
    string entityName,
    Expression<Func<T, int>> identity,
    params string[] aliases)
{
    var entity = Metadata.Entities.Single(e =>
        string.Equals(e.Name, entityName, StringComparison.OrdinalIgnoreCase));

    builder.Entity<T>(entity.EntityId, entity.Name, e =>
    {
        e.Identity(identity);
        e.Aliases(aliases);
    });
}


}
