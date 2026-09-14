using Foundgine.SupplyChain.Advanced.Data;
using Foundgine.SupplyChain.Advanced.Domain;
using Xunit;

namespace Foundgine.SupplyChain.Advanced.Tests;

public sealed class SupplyChainDataFixtureTests
{
    [Fact]
    public void Seed_data_contains_a_BOM_component_cycle()
    {
        var d = SupplyChainData.Seed();
        var byParent = d.Components.ToLookup(c => c.ParentProductId, c => c.ComponentProductId);

        bool HasCycle(ProductId start)
        {
            var visited = new HashSet<ProductId>();
            var stack = new Stack<ProductId>([start]);
            while (stack.Count > 0)
            {
                var current = stack.Pop();
                foreach (var child in byParent[current])
                {
                    if (child == start) return true;
                    if (visited.Add(child)) stack.Push(child);
                }
            }

            return false;
        }

        Assert.Contains(d.Products, p => HasCycle(p.Id));
    }

    [Fact]
    public void Seed_data_contains_a_warehouse_belonging_to_a_different_tenant()
    {
        var d = SupplyChainData.Seed();
        var tenants = d.Warehouses.Select(w => w.TenantId).Distinct().ToArray();
        Assert.True(tenants.Length > 1);
    }

    [Fact]
    public void Seed_data_contains_an_expired_supplier_certification()
    {
        var d = SupplyChainData.Seed();
        var referenceDate = new DateOnly(2026, 8, 27);
        Assert.Contains(d.Certifications, c => c.ValidTo < referenceDate);
    }

    [Fact]
    public void Seed_data_contains_a_cancelled_purchase_order()
    {
        var d = SupplyChainData.Seed();
        Assert.Contains(d.PurchaseOrders, po => po.Status == PurchaseOrderStatus.Cancelled);
    }

    [Fact]
    public void Seed_data_contains_a_partially_received_or_delayed_shipment()
    {
        var d = SupplyChainData.Seed();
        Assert.Contains(d.Shipments, s => s.Status is ShipmentStatus.Delayed or ShipmentStatus.PartiallyReceived);
    }

    [Fact]
    public void Seed_data_contains_quarantined_inventory()
    {
        var d = SupplyChainData.Seed();
        Assert.Contains(d.Inventory, lot => lot.Quarantined > 0);
    }

    [Fact]
    public void Seed_data_contains_reserved_inventory_that_is_unavailable_for_new_demand()
    {
        var d = SupplyChainData.Seed();
        Assert.Contains(d.Inventory, lot => lot.Reserved > 0);
    }

    [Fact]
    public void Every_purchase_order_line_references_a_purchase_order_that_exists()
    {
        var d = SupplyChainData.Seed();
        var ids = d.PurchaseOrders.Select(po => po.Id).ToHashSet();
        Assert.All(d.PurchaseOrderLines, line => Assert.Contains(line.PurchaseOrderId, ids));
    }

    [Fact]
    public void Every_shipment_references_a_purchase_order_that_exists()
    {
        var d = SupplyChainData.Seed();
        var ids = d.PurchaseOrders.Select(po => po.Id).ToHashSet();
        Assert.All(d.Shipments, shipment => Assert.Contains(shipment.PurchaseOrderId, ids));
    }

    [Fact]
    public void Every_customer_order_line_references_a_customer_order_that_exists()
    {
        var d = SupplyChainData.Seed();
        var ids = d.CustomerOrders.Select(o => o.Id).ToHashSet();
        Assert.All(d.CustomerOrderLines, line => Assert.Contains(line.CustomerOrderId, ids));
    }
}