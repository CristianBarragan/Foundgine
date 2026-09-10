using Foundgine.SupplyChain.Advanced.Data;
using Foundgine.SupplyChain.Advanced.Semantics;
using Foundgine.SupplyChain.Advanced.Scenarios;
using Foundgine.SupplyChain.Advanced.Domain;

Console.WriteLine("Foundgine Supply Chain — semantic execution showcase");
Console.WriteLine("===================================================");

var semanticModel = SupplyChainSemanticModel.Build();
var data = SupplyChainData.Seed();
var auth = new AuthorizationContext("tenant-a", new HashSet<WarehouseId> { new(1), new(2) }, true, true);

Console.WriteLine($"Semantic entities: {semanticModel.Entities.Count}");
Console.WriteLine(
    $"Products: {data.Products.Count}; suppliers: {data.Suppliers.Count}; warehouses: {data.Warehouses.Count}");
Console.WriteLine();

Console.WriteLine("01 — Recursive supplier risk");
foreach (var x in SupplyChainScenarios.RecursiveSupplierRisk(data, new ProductId(1), auth))
    Console.WriteLine(
        $"  Product={x.ProductId.Value} Supplier={x.SupplierId.Value} Depth={x.Depth} Cycle={x.CycleDetected}");

Console.WriteLine();
Console.WriteLine("02 — Fulfillment planning (14-day horizon)");
foreach (var x in SupplyChainScenarios.FulfillmentPlanning(data, new DateOnly(2026, 8, 27), auth))
    Console.WriteLine(
        $"  {x.Sku}: demand={x.Demand} available={x.Available} inbound={x.ProjectedInbound} shortage={x.ProjectedShortage}");

Console.WriteLine();
Console.WriteLine("03 — Adversarial invariants");
SupplyChainScenarios.AssertAdversarialInvariants(data, auth);

Console.WriteLine();
Console.WriteLine("04 — Semantic intent");
Console.WriteLine("  Find the top products likely to cause fulfillment failure within 14 days,");
Console.WriteLine("  including recursive BOM dependencies, delayed inbound supply, authorization,");
Console.WriteLine("  certification validity, aggregation, stable ordering and bounded traversal.");