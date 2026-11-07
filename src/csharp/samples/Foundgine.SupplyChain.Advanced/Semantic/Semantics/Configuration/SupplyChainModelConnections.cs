using Foundgine.Core.Semantic.Configuration;
using Foundgine.SupplyChain.Advanced.Domain;

namespace Foundgine.SupplyChain.Advanced.Semantics.Configuration;

// Model-to-entity connections: what a model may visit, how members correspond, and who may
// follow the connection. A purchase order reaches its supplier through SupplierId, and only a
// caller from the same tenant as the supplier may follow it.

/// <summary>The caller facts the supplier connection's authorization predicate reads.</summary>
public sealed record SupplierAccessContext(string TenantId);

public sealed class SupplyChainModelConnections : FoundgineModelConnectionConfiguration<PurchaseOrder>
{
    public override void Configure(FoundgineModelConnectionBuilder<PurchaseOrder> connections) =>
        connections
            .HasConnection<Supplier>(x => x.SupplierId)
            .HasName("supplier")
            .Maps(x => x.SupplierId, x => x.Id)
            .Authorize<SupplierAccessContext>(
                "SameTenantSupplier",
                (context, supplier) => context.TenantId == supplier.TenantId);
}
