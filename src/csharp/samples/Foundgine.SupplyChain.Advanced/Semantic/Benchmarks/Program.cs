// Foundgine Supply Chain Semantic — pipeline weight benchmark.
//
// Answers one question: "how heavy is all the bits together?" — i.e. what
// does it cost, in wall-clock time and managed allocations, to actually walk
// the sample's own documented pipeline
//
//     Metadata -> SemanticModel.Discover() -> semantic enrichment ->
//     authorization -> open intent -> resolution / planning
//
// end to end, rather than any single piece of it in isolation. Every stage
// below is exercised individually first (so a regression can be attributed
// to a specific boundary), and then the same stages are chained into one
// "FullPipeline" unit that is the headline number.
//
// This harness is deliberately dependency-free (Stopwatch +
// GC.GetAllocatedBytesForCurrentThread instead of BenchmarkDotNet) so it
// builds and runs anywhere the sample itself builds, with no extra NuGet
// restore. It is a coarse, order-of-magnitude instrument, not a substitute
// for BenchmarkDotNet's statistical rigor — treat the numbers as "did this
// stage get 3x heavier", not as publishable micro-benchmark results.
//
// Run:
//   dotnet run -c Release --project Benchmarks
//   dotnet run -c Release --project Benchmarks -- --iterations 5000 --warmup 200

using System.Globalization;
using System.Text.Json;
using Foundgine.Core.Semantic.Capabilities;
using Foundgine.Core.Semantic.Intent;
using Foundgine.Core.Semantic.Mutation;
using Foundgine.Core.Semantic.Query;
using Foundgine.Core.Semantic.Resolution;
using Foundgine.SupplyChain.Advanced.Authorization;
using Foundgine.SupplyChain.Advanced.Data;
using Foundgine.SupplyChain.Advanced.Domain;
using Foundgine.SupplyChain.Advanced.Scenarios;
using Foundgine.SupplyChain.Advanced.Semantics;

var (iterations, warmup) = ParseArgs(args);

Console.WriteLine("Foundgine Supply Chain Semantic — pipeline weight benchmark");
Console.WriteLine("=============================================================");
Console.WriteLine($"iterations={iterations} warmup={warmup} gcServer={System.Runtime.GCSettings.IsServerGC}");
Console.WriteLine();

PrintStructuralSize();

var seedData = SupplyChainData.Seed();
var seedAuth = new AuthorizationContext(
    "tenant-a", new HashSet<WarehouseId> { new(1), new(2) }, CanReadSupplierRisk: true, CanWritePurchasing: true);

var results = new List<StageResult>
{
    Measure("01 Metadata catalog access", () => Touch(SupplyChainSemanticModel.Metadata.Entities.Count())),
    Measure("02 SemanticModel.Build() (discover + traversal enrichment)",
        () => Touch(SupplyChainSemanticModel.Build().Entities.Count)),
    Measure("03 Freeze() + CreateSnapshot()", () =>
    {
        var model = SupplyChainSemanticModel.Build();
        return Touch(model.Freeze().CreateSnapshot() is null ? 0 : 1);
    }),
    Measure("04 Authorization policy construction (x4 roles)", () =>
    {
        var sum = 0;
        foreach (var role in SampleRoles.All)
            sum += SupplyChainAuthorization.Create("tenant-a", role) is null ? 0 : 1;
        return Touch(sum);
    }),
    Measure("05 Capability contract discovery (x4 roles)", () =>
    {
        var model = SupplyChainSemanticModel.Model; // cached — isolates discovery cost from Build()
        var sum = 0;
        foreach (var role in SampleRoles.All)
        {
            var policy = SupplyChainAuthorization.Create("tenant-a", role);
            sum += SemanticCapabilityContractDiscovery.Describe(model, policy).Capabilities.Count;
        }

        return Touch(sum);
    }),
    Measure("06 Open intent: shallow read (Product.shipments.Status)",
        () => Touch(ResolveShallowReadIntent().Nodes.Count)),
    Measure("07 Open intent: deep read (Product.supplierIncidents, 5 hops)",
        () => Touch(ResolveDeepReadIntent().Nodes.Count)),
    Measure("08 Open intent: 4-op nested mutation build + plan", () => Touch(BuildAndPlanMutation().Operations.Count)),
    Measure("09 Scenario: recursive supplier risk (BOM cycle)", () =>
        Touch(SupplyChainScenarios.RecursiveSupplierRisk(seedData, new ProductId(1), seedAuth).Count)),
    Measure("10 Scenario: fulfillment planning (14-day horizon)", () =>
        Touch(SupplyChainScenarios.FulfillmentPlanning(seedData, new DateOnly(2026, 8, 27), seedAuth).Count)),
    Measure("11 Scenario: adversarial invariants", () =>
        WithSilencedConsole(() =>
        {
            SupplyChainScenarios.AssertAdversarialInvariants(seedData, seedAuth);
            return Touch(1);
        })),
    Measure("12 FULL PIPELINE (all of the above, one request's worth)", () => WithSilencedConsole(RunFullPipelineOnce)),
};

Console.WriteLine();
PrintTable(results);
Console.WriteLine();
PrintFullPipelineShare(results);
Console.WriteLine();
var efficiency = PrintAndBuildEfficiencyEstimate();
WriteJsonReport(results, efficiency);

// ---- stage bodies -------------------------------------------------------

int RunFullPipelineOnce()
{
    // A single, self-contained "cold-ish" pass through every documented
    // pipeline boundary: metadata -> semantics -> authorization -> intent ->
    // resolution/planning -> the sample's hardest scenarios. This is the
    // number that answers "how heavy is all the bits together", as opposed
    // to any individual stage above.
    var sum = 0;

    var model = SupplyChainSemanticModel.Build();
    sum += model.Entities.Count;

    var snapshot = model.Freeze().CreateSnapshot();
    sum += snapshot is null ? 0 : 1;

    foreach (var role in SampleRoles.All)
    {
        var policy = SupplyChainAuthorization.Create("tenant-a", role);
        sum += SemanticCapabilityContractDiscovery.Describe(model, policy).Capabilities.Count;
    }

    sum += ResolveShallowReadIntent().Nodes.Count;
    sum += ResolveDeepReadIntent().Nodes.Count;
    sum += BuildAndPlanMutation().Operations.Count;

    sum += SupplyChainScenarios.RecursiveSupplierRisk(seedData, new ProductId(1), seedAuth).Count;
    sum += SupplyChainScenarios.FulfillmentPlanning(seedData, new DateOnly(2026, 8, 27), seedAuth).Count;
    SupplyChainScenarios.AssertAdversarialInvariants(seedData, seedAuth);

    return Touch(sum);
}

Foundgine.Core.Semantic.SemanticGraph ResolveShallowReadIntent()
{
    var model = SupplyChainSemanticModel.Model;
    var request = new ReadIntent(
        "Product",
        [new ReadSelection(Relationship: "shipments", Children: [new ReadSelection(Field: "Status")])]);
    var compiled = new ReadIntentCompiler(model).Compile(request);
    return new SemanticRequestResolver(model.Freeze().CreateSnapshot()).Resolve(compiled);
}

Foundgine.Core.Semantic.SemanticGraph ResolveDeepReadIntent()
{
    var model = SupplyChainSemanticModel.Model;
    var request = new ReadIntent(
        "Product",
        [new ReadSelection(Relationship: "supplierIncidents", Children: [new ReadSelection(Field: "Severity")])]);
    var compiled = new ReadIntentCompiler(model).Compile(request);
    return new SemanticRequestResolver(model.Freeze().CreateSnapshot()).Resolve(compiled);
}

SemanticMutationPlan BuildAndPlanMutation()
{
    var model = SupplyChainSemanticModel.Model;
    var graph = new SemanticMutationIntentBuilder(model)
        .Create("PurchaseOrder", "order")
        .Set("SupplierId", 1)
        .Set("WarehouseId", 1)
        .Set("Status", "Open")
        .Return("Id")
        .Create("PurchaseOrderLine", "line")
        .SetFrom("PurchaseOrderId", "order", "Id")
        .Set("ProductId", 1)
        .Set("Quantity", 25m)
        .Return("Id", "PurchaseOrderId")
        .Create("Shipment", "shipment")
        .SetFrom("PurchaseOrderId", "order", "Id")
        .Set("ExpectedArrival", new DateTime(2026, 9, 5))
        .Set("Status", "Planned")
        .Set("Quantity", 25m)
        .Return("Id", "PurchaseOrderId")
        .Update("PurchaseOrder")
        .Set("Status", "Open")
        .Where("Id", SemanticFilterOperator.Eq, 1)
        .Return("Id")
        .Build();

    return new SemanticMutationPlanner().Plan(graph);
}

// ---- harness --------------------------------------------------------------

StageResult Measure(string name, Func<int> action)
{
    // Warm up the JIT and any lazily-built static state (generated metadata
    // registries, cached delegates, ...) before any sample is recorded.
    for (var i = 0; i < warmup; i++) action();

    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();

    var checksum = 0L;
    var elapsedTicksTotal = 0L;
    var bytesTotal = 0L;
    var minTicks = long.MaxValue;
    var maxTicks = long.MinValue;
    var sw = new System.Diagnostics.Stopwatch();

    for (var i = 0; i < iterations; i++)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        sw.Restart();
        checksum += action();
        sw.Stop();
        var after = GC.GetAllocatedBytesForCurrentThread();

        elapsedTicksTotal += sw.ElapsedTicks;
        bytesTotal += after - before;
        if (sw.ElapsedTicks < minTicks) minTicks = sw.ElapsedTicks;
        if (sw.ElapsedTicks > maxTicks) maxTicks = sw.ElapsedTicks;
    }

    var freq = System.Diagnostics.Stopwatch.Frequency;
    double ticksToMicros(long ticks) => ticks * 1_000_000.0 / freq;

    return new StageResult(
        name,
        AvgMicros: ticksToMicros(elapsedTicksTotal) / iterations,
        MinMicros: ticksToMicros(minTicks == long.MaxValue ? 0 : minTicks),
        MaxMicros: ticksToMicros(maxTicks == long.MinValue ? 0 : maxTicks),
        AvgBytes: (double)bytesTotal / iterations,
        Checksum: checksum);
}

int Touch(int value) => value; // keeps the JIT from proving the call is dead code.

T WithSilencedConsole<T>(Func<T> action)
{
    // AssertAdversarialInvariants (and therefore the full pipeline) logs a
    // PASS line per invariant on every call; that is useful once, not
    // thousands of times inside a timed loop.
    var original = Console.Out;
    Console.SetOut(TextWriter.Null);
    try
    {
        return action();
    }
    finally
    {
        Console.SetOut(original);
    }
}

// ---- token / agent-work reduction estimate ---------------------------------
//
// Everything above measures *time and allocations* for a single process; it
// has no conventional (REST/GraphQL-per-entity) counterpart running next to
// it, so there is nothing here to measure a live comparison against. What
// follows is instead a MODELED estimate, grounded in the real structural
// data this benchmark already has on hand (actual resolved SemanticGraph
// node/field counts, actual per-entity field counts from the semantic
// model), rather than an assumed constant multiplier:
//
//   - Foundgine tool calls: 1 per read (a single ReadIntent resolution),
//     because that's what stages 06/07 above actually do.
//   - Modeled conventional tool calls: 1 per node in the resolved graph —
//     the classic N+1 pattern of a per-record REST/GraphQL fetch, which is
//     exactly the shape Foundgine's single traversal replaces.
//   - Token load uses the field-selection difference that's actually in the
//     data: Foundgine's SemanticGraphNode.Fields is the field set the intent
//     asked for; a conventional full-resource endpoint would instead return
//     every field the entity has (from the semantic model's own field
//     count). Both are converted to a token estimate with the same
//     TokensPerField assumption, which is documented and can be overridden.
//
// TokensPerField (~6) approximates a typical `"fieldName": value` JSON pair
// under the same chars/4-vs-words*1.3 heuristic the AgentEndToEnd benchmarks
// use (see AgentEndToEnd/Run1/Program.cs TokenEstimator and
// AgentEndToEnd/scripts/estimate_tokens.py) — it is a coarse constant, not a
// measured tokenizer count, and is reported alongside the raw field counts
// so a reader can substitute their own assumption.
const double TokensPerField = 6.0;
const double NodeEnvelopeTokens = 2.0; // per-node id/type overhead, both sides

EfficiencyEstimate PrintAndBuildEfficiencyEstimate()
{
    var model = SupplyChainSemanticModel.Model;
    var fieldCountByEntity = model.Entities.ToDictionary(e => e.Id, e => e.Fields.Count);

    var shallow = ResolveShallowReadIntent();
    var deep = ResolveDeepReadIntent();

    ReadScenarioEstimate Estimate(string name, Foundgine.Core.Semantic.SemanticGraph graph)
    {
        var nodeCount = graph.Nodes.Count;
        var selectedFields = graph.Nodes.Sum(n => n.Fields.Count);
        var fullFields = graph.Nodes.Sum(n => fieldCountByEntity.TryGetValue(n.EntityId, out var c) ? c : 0);

        var foundgineTokens = selectedFields * TokensPerField + nodeCount * NodeEnvelopeTokens;
        var conventionalTokens = fullFields * TokensPerField + nodeCount * NodeEnvelopeTokens;
        var conventionalToolCalls = Math.Max(1, nodeCount); // N+1: one fetch per record
        const int foundgineToolCalls = 1; // one ReadIntent resolution

        return new ReadScenarioEstimate(
            name, nodeCount, selectedFields, fullFields,
            Math.Round(foundgineTokens, 1), Math.Round(conventionalTokens, 1),
            conventionalTokens > 0 ? Math.Round((1 - foundgineTokens / conventionalTokens) * 100, 1) : 0,
            foundgineToolCalls, conventionalToolCalls,
            Math.Round((1 - (double)foundgineToolCalls / conventionalToolCalls) * 100, 1));
    }

    var scenarios = new[]
    {
        Estimate("Shallow read (Product.shipments.Status)", shallow),
        Estimate("Deep read (Product.supplierIncidents.Severity, 5 hops)", deep),
    };

    Console.WriteLine("Token / agent-work reduction estimate (modeled, see caveats below)");
    Console.WriteLine(new string('-', 96));
    foreach (var s in scenarios)
    {
        Console.WriteLine($"  {s.Name}");
        Console.WriteLine(
            $"    nodes touched: {s.NodeCount}   selected fields: {s.SelectedFieldCount}   full-entity fields: {s.FullFieldCount}");
        Console.WriteLine(
            $"    est. context load: Foundgine ~{s.FoundgineTokens} tok  vs  conventional-full-resource ~{s.ConventionalTokens} tok  ({s.ContextLoadReductionPercent:N1}% lower)");
        Console.WriteLine(
            $"    tool calls: Foundgine {s.FoundgineToolCalls}  vs  conventional (N+1) {s.ConventionalToolCalls}  ({s.ToolCallReductionPercent:N1}% lower)");
    }

    Console.WriteLine();
    Console.WriteLine("Caveats:");
    Console.WriteLine(
        "  - Modeled, not measured: no conventional REST/GraphQL endpoint runs in this process to compare against.");
    Console.WriteLine(
        "  - TokensPerField (~6) and per-node envelope (~2) are coarse JSON-payload assumptions, not a real tokenizer count.");
    Console.WriteLine(
        "  - \"Conventional\" assumes one request per record (N+1) and a full-resource (all-fields) response shape, which is");
    Console.WriteLine(
        "    the common default for hand-written REST/GraphQL resolvers this sample's docs compare Foundgine against.");
    Console.WriteLine(
        "  - For a measured (not modeled) comparison, see src/csharp/benchmarks/AgentEndToEnd/Run1-5, which execute both flows live.");

    return new EfficiencyEstimate(scenarios,
        "Modeled estimate grounded in this run's actual SemanticGraph node/field counts, not a live conventional comparison. " +
        "TokensPerField=6, NodeEnvelopeTokens=2 (coarse JSON-payload assumptions). Conventional side assumes N+1 tool calls and full-resource field selection.");
}

void WriteJsonReport(IReadOnlyList<StageResult> stageResults, EfficiencyEstimate efficiency)
{
    var reportDir = Environment.GetEnvironmentVariable("SUPPLY_CHAIN_SEMANTIC_REPORT_DIRECTORY")
                    ?? Path.Combine(AppContext.BaseDirectory, "reports");
    Directory.CreateDirectory(reportDir);

    var full = stageResults[^1];
    var report = new
    {
        schemaVersion = 1,
        utc = DateTimeOffset.UtcNow,
        iterations,
        warmup,
        stages = stageResults.Select(s => new
            { s.Name, s.AvgMicros, s.MinMicros, s.MaxMicros, avgKb = s.AvgBytes / 1024.0 }),
        fullPipeline = new { full.AvgMicros, avgKb = full.AvgBytes / 1024.0 },
        efficiencyEstimate = efficiency,
    };

    var path = Path.Combine(reportDir, "pipeline-benchmark.json");
    File.WriteAllText(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine();
    Console.WriteLine($"Wrote {path}");
}

void PrintStructuralSize()
{
    var model = SupplyChainSemanticModel.Build();
    var entityCount = model.Entities.Count;
    var fieldCount = model.Entities.Sum(e => e.Fields.Count);
    var relationshipCount = model.Entities.Sum(e => e.Relationships.Count);
    var traversalCount = model.Traversals.Count;

    Console.WriteLine("Structural weight (what got built, independent of how long it took)");
    Console.WriteLine($"  Entities:            {entityCount}");
    Console.WriteLine($"  Fields:              {fieldCount}");
    Console.WriteLine($"  Direct relationships:{relationshipCount,4}");
    Console.WriteLine($"  Logical traversals:  {traversalCount}");
    foreach (var role in SampleRoles.All)
    {
        var policy = SupplyChainAuthorization.Create("tenant-a", role);
        var capabilities = SemanticCapabilityContractDiscovery.Describe(model, policy).Capabilities.Count;
        Console.WriteLine($"  Capabilities ({role,-19}): {capabilities}");
    }
}

void PrintTable(IReadOnlyList<StageResult> stageResults)
{
    Console.WriteLine($"{"Stage",-58} {"avg µs",8} {"min µs",8} {"max µs",8} {"avg KB",8}");
    Console.WriteLine(new string('-', 96));
    foreach (var r in stageResults)
    {
        Console.WriteLine(
            $"{r.Name,-58} " +
            $"{r.AvgMicros.ToString("N1", CultureInfo.InvariantCulture),8} " +
            $"{r.MinMicros.ToString("N1", CultureInfo.InvariantCulture),8} " +
            $"{r.MaxMicros.ToString("N1", CultureInfo.InvariantCulture),8} " +
            $"{(r.AvgBytes / 1024.0).ToString("N2", CultureInfo.InvariantCulture),8}");
    }
}

void PrintFullPipelineShare(IReadOnlyList<StageResult> stageResults)
{
    var full = stageResults[^1];
    var stages = stageResults.Take(stageResults.Count - 1).ToArray();
    var sumOfParts = stages.Sum(s => s.AvgMicros);

    Console.WriteLine("Full-pipeline sanity check");
    Console.WriteLine($"  Sum of individual stages: {sumOfParts:N1} µs");
    Console.WriteLine($"  Measured full pipeline:   {full.AvgMicros:N1} µs");
    Console.WriteLine($"  Full pipeline allocates:  {full.AvgBytes / 1024.0:N2} KB/request");
    Console.WriteLine();
    Console.WriteLine("Heaviest stages by share of the full pipeline:");
    foreach (var s in stages.OrderByDescending(s => s.AvgMicros).Take(3))
        Console.WriteLine($"  {s.Name,-58} {s.AvgMicros / full.AvgMicros:P1}");
}

(int iterations, int warmup) ParseArgs(string[] a)
{
    var iters = 2000;
    var warm = 100;
    for (var i = 0; i < a.Length - 1; i++)
    {
        if (a[i] == "--iterations" && int.TryParse(a[i + 1], out var it)) iters = it;
        if (a[i] == "--warmup" && int.TryParse(a[i + 1], out var wu)) warm = wu;
    }

    return (iters, warm);
}

internal sealed record StageResult(
    string Name,
    double AvgMicros,
    double MinMicros,
    double MaxMicros,
    double AvgBytes,
    long Checksum);

internal sealed record ReadScenarioEstimate(
    string Name,
    int NodeCount,
    int SelectedFieldCount,
    int FullFieldCount,
    double FoundgineTokens,
    double ConventionalTokens,
    double ContextLoadReductionPercent,
    int FoundgineToolCalls,
    int ConventionalToolCalls,
    double ToolCallReductionPercent);

internal sealed record EfficiencyEstimate(IReadOnlyList<ReadScenarioEstimate> Scenarios, string Method);

internal static class SampleRoles
{
    // The full role set the sample ships (Authorization/SupplyChainAuthorization.cs),
    // walked on every capability-discovery/full-pipeline pass so the benchmark
    // measures the same authorization surface area a real deployment would.
    public static readonly SupplyChainRole[] All =
    [
        SupplyChainRole.Customer,
        SupplyChainRole.Analyst,
        SupplyChainRole.WarehouseOperator,
        SupplyChainRole.SupplyChainManager
    ];
}