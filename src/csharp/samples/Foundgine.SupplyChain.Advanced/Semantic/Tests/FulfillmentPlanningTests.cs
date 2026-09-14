using Foundgine.SupplyChain.Advanced.Data;
using Foundgine.SupplyChain.Advanced.Domain;
using Foundgine.SupplyChain.Advanced.Scenarios;
using Xunit;

namespace Foundgine.SupplyChain.Advanced.Tests;

public sealed class FulfillmentPlanningTests
{
    [Fact]
    public void Fulfillment_planning_excludes_reserved_and_quarantined_inventory()
    {
        var data = SupplyChainData.Seed();
        var auth = new AuthorizationContext("tenant-a", new HashSet<WarehouseId> { new(1), new(2) }, true, true);

        var risks = SupplyChainScenarios.FulfillmentPlanning(data, new DateOnly(2026, 8, 27), auth);

        // Product 4 has 70 on hand, 20 reserved and 10 quarantined in the
        // authorized warehouse (warehouse 1). The usable quantity there is
        // therefore 40, not 70. Warehouse 3's 5,000 units belong to tenant-b
        // and must be excluded from this tenant's usable inventory.
        var inventoryForProduct4 = data.Inventory
            .Where(x => x.ProductId == new ProductId(4) && auth.AllowedWarehouses.Contains(x.WarehouseId))
            .Sum(x => Math.Max(0, x.OnHand - x.Reserved - x.Quarantined));
        Assert.Equal(40, inventoryForProduct4);
        Assert.DoesNotContain(risks, x => x.ProductId == new ProductId(4));
    }

    [Fact]
    public void Fulfillment_planning_does_not_use_cancelled_purchase_orders()
    {
        var data = SupplyChainData.Seed();
        data.CustomerOrderLines.Add(new CustomerOrderLine(new CustomerOrderLineId(9000), new CustomerOrderId(700),
            new ProductId(4), 5000));
        var auth = new AuthorizationContext("tenant-a", new HashSet<WarehouseId> { new(1), new(2) }, true, true);

        var risks = SupplyChainScenarios.FulfillmentPlanning(data, new DateOnly(2026, 8, 27), auth);

        var risk = Assert.Single(risks, x => x.ProductId == new ProductId(4));
        Assert.Equal(3960, risk.ProjectedShortage); // 5000 - 40 usable - 1000 inbound.
    }

    [Fact]
    public void Fulfillment_planning_excludes_inventory_in_an_unauthorized_warehouse()
    {
        var data = SupplyChainData.Seed();
        data.CustomerOrderLines.Add(new CustomerOrderLine(new CustomerOrderLineId(9001), new CustomerOrderId(700),
            new ProductId(4), 4500));
        var auth = new AuthorizationContext("tenant-a", new HashSet<WarehouseId> { new(1), new(2) }, true, true);

        var risks = SupplyChainScenarios.FulfillmentPlanning(data, new DateOnly(2026, 8, 27), auth);
        var risk = Assert.Single(risks, x => x.ProductId == new ProductId(4));

        // Tenant-B's 5,000 units in warehouse 3 must not satisfy tenant-A demand.
        Assert.Equal(3460, risk.ProjectedShortage);
    }

    [Fact]
    public void Fulfillment_results_are_stably_ordered_by_shortage_then_product_id()
    {
        var data = SupplyChainData.Seed();
        var auth = new AuthorizationContext("tenant-a", new HashSet<WarehouseId> { new(1), new(2) }, true, true);

        var risks = SupplyChainScenarios.FulfillmentPlanning(data, new DateOnly(2026, 8, 27), auth);

        Assert.True(
            risks.SequenceEqual(risks.OrderByDescending(x => x.ProjectedShortage).ThenBy(x => x.ProductId.Value)));
    }
}