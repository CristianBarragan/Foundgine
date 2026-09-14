# Pipeline weight benchmark

Answers one question: **how heavy is all the bits together?**

The sample's own architecture is a pipeline —

```text
Metadata -> SemanticModel.Discover() -> semantic enrichment ->
authorization -> open intent -> resolution / planning
```

— and each boundary is well covered individually by the test suite. This
benchmark instead walks the whole thing end to end, the way a real request
would, and reports both time and managed allocations per stage plus for the
full pipeline as one unit.

## Run it

```bash
dotnet run -c Release --project Benchmarks
```

Optional flags:

```bash
dotnet run -c Release --project Benchmarks -- --iterations 5000 --warmup 200
```

Always run with `-c Release`. The project also forces `Optimize=true` so
`dotnet run` without `-c Release` still measures optimized code, but you
still want Release for representative GC behavior.

## What it measures

Twelve stages, in increasing order of scope:

1. Metadata catalog access (the cheapest possible baseline)
2. `SemanticModel.Build()` — structural discovery + the two logical
   traversal enrichments (`Product.shipments`, `Product.supplierIncidents`)
3. `Freeze()` + `CreateSnapshot()`
4. Authorization policy construction, for all four sample roles
5. Capability contract discovery, for all four sample roles
6. Open-intent read resolution: a shallow traversal
   (`Product.shipments.Status`)
7. Open-intent read resolution: the deeper 4-hop traversal
   (`Product.supplierIncidents.Severity`)
8. Open-intent mutation: build + plan a 4-operation nested mutation graph
9. The recursive-BOM-with-cycle-detection scenario
10. The fulfillment-planning scenario
11. The adversarial invariant suite
12. **The full pipeline** — every stage above, chained into one pass, which
    is the headline "how heavy is all the bits together" number

Each stage reports average/min/max microseconds and average KB allocated
per call, after a configurable warm-up. A final section cross-checks the
full-pipeline number against the sum of the individual stages and names the
three heaviest contributors, so a regression in any one boundary is visible
both on its own and as a share of the whole request.

## Why not BenchmarkDotNet

This harness is intentionally dependency-free (`Stopwatch` +
`GC.GetAllocatedBytesForCurrentThread`) so it builds and runs anywhere the
sample itself builds, without adding a NuGet package restore step. It is a
coarse, order-of-magnitude instrument — good for "did this stage just get
3x heavier", not a substitute for BenchmarkDotNet's statistical rigor. If
the project later wants percentile distributions, outlier removal, or
disassembly reports, swap this `Program.cs` for a `[MemoryDiagnoser]`
BenchmarkDotNet class; the stage bodies here can be copied over almost
verbatim as `[Benchmark]` methods.
