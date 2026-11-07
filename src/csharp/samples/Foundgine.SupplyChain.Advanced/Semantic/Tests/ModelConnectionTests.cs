using Foundgine.Core.Abstractions;
using Foundgine.SupplyChain.Advanced.Semantics;
using Foundgine.SupplyChain.Advanced.Semantics.Configuration;
using Xunit;

namespace Foundgine.SupplyChain.Advanced.Tests;

public sealed class ModelConnectionTests
{
    [Fact]
    public void Purchase_order_connects_to_supplier_with_tenant_authorization()
    {
        var registry = new SupplyChainModel().Build();

        var connection = Assert.Single(registry.Connections);
        Assert.Equal("supplier", connection.Name);
        Assert.Equal(EntityId.Create("Supplier"), connection.Target);

        var field = Assert.Single(connection.Fields!);
        Assert.Equal("SupplierId", field.SourceMember);
        Assert.Equal("Id", field.TargetMember);

        var authorization = Assert.Single(registry.Authorizations);
        Assert.Equal(connection.Id, authorization.ConnectionId);
        Assert.Equal(typeof(SupplierAccessContext), authorization.ContextType);
        Assert.Equal(AuthorizationPredicateKind.Equal, authorization.Predicate!.Kind);
    }
}
