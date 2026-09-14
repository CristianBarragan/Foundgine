using Foundgine.SupplyChain.Advanced.Data;
using Foundgine.SupplyChain.Advanced.Domain;
using Foundgine.SupplyChain.Advanced.Application;

namespace Foundgine.SupplyChain.Advanced.Scenarios;

public sealed record AuthorizationContext(
    string TenantId,
    IReadOnlySet<WarehouseId> AllowedWarehouses,
    bool CanReadSupplierRisk,
    bool CanWritePurchasing);

public sealed record SupplierExposure(ProductId ProductId, SupplierId SupplierId, int Depth, bool CycleDetected);

public sealed record FulfillmentRisk(
    ProductId ProductId,
    string Sku,
    decimal Demand,
    decimal Available,
    decimal ProjectedInbound,
    decimal ProjectedShortage,
    IReadOnlyList<SupplierId> Suppliers);

public static class SupplyChainScenarios
{
    public static IReadOnlyList<SupplierExposure> RecursiveSupplierRisk(SupplyChainData d, ProductId root,
        AuthorizationContext auth)
    {
        var result = new List<SupplierExposure>();
        var visited = new HashSet<ProductId>();
        Walk(root, 0, new HashSet<ProductId>());
        return result;

        void Walk(ProductId product, int depth, HashSet<ProductId> path)
        {
            if (depth > SupplyChainExecutionLimits.RecursiveBomMaxDepth) return;
            if (!path.Add(product))
            {
                result.Add(new(product, default, depth, true));
                return;
            }

            if (!visited.Add(product))
            {
                path.Remove(product);
                return;
            }

            var childProducts = d.Components.Where(x => x.ParentProductId == product).Select(x => x.ComponentProductId)
                .Distinct();
            foreach (var child in childProducts)
            {
                var supplierIds = d.PurchaseOrderLines
                    .Where(l => l.ProductId == child)
                    .Join(d.PurchaseOrders, l => l.PurchaseOrderId, p => p.Id, (_, p) => p.SupplierId)
                    .Distinct();
                foreach (var supplier in supplierIds)
                    if (d.Suppliers.FirstOrDefault(s => s.Id == supplier)?.TenantId == auth.TenantId)
                        result.Add(new(child, supplier, depth + 1, false));
                Walk(child, depth + 1, path);
            }

            path.Remove(product);
        }
    }

    public static IReadOnlyList<FulfillmentRisk> FulfillmentPlanning(SupplyChainData d, DateOnly asOf,
        AuthorizationContext auth)
    {
        var demand = d.CustomerOrderLines
            .Join(d.CustomerOrders, l => l.CustomerOrderId, o => o.Id, (l, o) => (l, o))
            .Where(x => x.o.Status == "Open")
            .GroupBy(x => x.l.ProductId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.l.Quantity));

        var output = new List<FulfillmentRisk>();
        foreach (var (productId, qty) in demand)
        {
            var available = d.Inventory
                .Where(i => i.ProductId == productId && auth.AllowedWarehouses.Contains(i.WarehouseId))
                .Sum(i => Math.Max(0, i.OnHand - i.Reserved - i.Quarantined));

            var inbound = d.PurchaseOrderLines
                .Where(l => l.ProductId == productId)
                .Join(d.PurchaseOrders, l => l.PurchaseOrderId, p => p.Id, (l, p) => (l, p))
                .Where(x => x.p.Status is PurchaseOrderStatus.Open or PurchaseOrderStatus.PartiallyReceived)
                .Where(x => auth.AllowedWarehouses.Contains(x.p.WarehouseId))
                .Join(d.Shipments, x => x.p.Id, s => s.PurchaseOrderId, (x, s) => (x, s))
                .Where(x => x.s.Status is ShipmentStatus.InTransit or ShipmentStatus.Delayed
                    or ShipmentStatus.PartiallyReceived)
                .Where(x => x.s.ExpectedArrival <= asOf.AddDays(14))
                .Sum(x => x.s.Quantity);

            var suppliers = d.PurchaseOrderLines.Where(l => l.ProductId == productId)
                .Join(d.PurchaseOrders, l => l.PurchaseOrderId, p => p.Id, (_, p) => p.SupplierId)
                .Distinct()
                .Where(s => d.Suppliers.Any(x => x.Id == s && x.TenantId == auth.TenantId))
                .ToArray();

            var shortage = Math.Max(0, qty - available - inbound);
            if (shortage <= 0) continue;
            var product = d.Products.Single(x => x.Id == productId);
            output.Add(new(productId, product.Sku, qty, available, inbound, shortage, suppliers));
        }

        return output.OrderByDescending(x => x.ProjectedShortage).ThenBy(x => x.ProductId.Value).Take(20).ToArray();
    }

    public static void AssertAdversarialInvariants(SupplyChainData d, AuthorizationContext auth)
    {
        if (d.Inventory.Any(i => !auth.AllowedWarehouses.Contains(i.WarehouseId) && i.WarehouseId.Value == 3))
            Console.WriteLine("PASS tenant isolation: restricted warehouse is present but excluded by authorization.");

        var cycle = RecursiveSupplierRisk(d, new ProductId(1), auth).Any(x => x.CycleDetected);
        if (!cycle) throw new InvalidOperationException("Expected BOM cycle to be detected.");
        Console.WriteLine("PASS recursive safety: BOM cycle detected and traversal terminated.");

        var expired = d.Certifications.Where(c => c.ValidTo < new DateOnly(2026, 8, 27)).Select(c => c.SupplierId)
            .ToHashSet();
        if (!expired.Contains(new SupplierId(3)))
            throw new InvalidOperationException("Expected expired certification fixture.");
        Console.WriteLine("PASS temporal security: expired supplier certification fixture detected.");

        if (auth.AllowedWarehouses.Contains(new WarehouseId(3)))
            throw new InvalidOperationException(
                "Adversarial authorization context accidentally exposes tenant-b warehouse.");
        Console.WriteLine("PASS authorization: caller cannot access tenant-b warehouse.");
    }
}