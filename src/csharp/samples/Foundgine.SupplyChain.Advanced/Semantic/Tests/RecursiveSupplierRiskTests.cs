using Foundgine.SupplyChain.Advanced.Data;
using Foundgine.SupplyChain.Advanced.Domain;
using Foundgine.SupplyChain.Advanced.Scenarios;
using Xunit;

namespace Foundgine.SupplyChain.Advanced.Tests;

public sealed class RecursiveSupplierRiskTests
{
    [Fact]
    public void Detects_the_seed_BOM_cycle_and_terminates_within_the_configured_depth()
    {
        var data = SupplyChainData.Seed();
        var auth = new AuthorizationContext("tenant-a", new HashSet<WarehouseId> { new(1), new(2) }, true, true);

        var result = SupplyChainScenarios.RecursiveSupplierRisk(data, new ProductId(1), auth);

        Assert.Contains(result, x => x.CycleDetected);
        Assert.All(result, x => Assert.InRange(x.Depth, 0, 5));
    }

    [Fact]
    public void Does_not_return_supplier_exposures_from_another_tenant()
    {
        var data = SupplyChainData.Seed();
        var auth = new AuthorizationContext("tenant-a", new HashSet<WarehouseId> { new(1), new(2) }, true, true);

        var result = SupplyChainScenarios.RecursiveSupplierRisk(data, new ProductId(1), auth);

        Assert.DoesNotContain(result, x => x.SupplierId == new SupplierId(3));
    }
}