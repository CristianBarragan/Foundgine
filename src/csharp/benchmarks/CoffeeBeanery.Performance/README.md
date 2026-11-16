# CoffeeBeanery Performance Benchmark

This benchmark is the reproducible performance harness for the CoffeeBeanery graph workload. It compares Foundgine with Hot Chocolate + EF Core against the same PostgreSQL fixture and the same GraphQL-shaped workloads.

## Current findings

The latest checked-in report set was generated on **15 August 2026**. The query workload at concurrency 32 reported:

1. **Query:** Hot Chocolate + EF Core reached 189.7 RPS; Foundgine without the provider-plan cache reached 2,427.1 RPS; Foundgine with the provider-plan cache reached 2,860.5 RPS. These values are specific to the published fixture and host.
2. **Whole-graph mutation:** at concurrency 32 / batch 50, Hot Chocolate + EF Core reached 56.4 HTTP RPS (2,820 logical mutations/s), Foundgine without the provider-plan cache reached 155.7 RPS (7,785 logical mutations/s), and Foundgine with the provider-plan cache reached 264.4 RPS (13,220 logical mutations/s). Request RPS and logical throughput must be kept distinct.
3. **Upsert + select:** the 15 August report set includes the corrected real upsert of existing deterministic customer rows followed by the same top-50/full-graph query. Treat the result as an initial measured snapshot, not a stable conclusion; repeat independent sessions before making broader claims.

The published JSON reports currently contain zero values for Docker CPU and memory measurements. They therefore do **not** support a comparative CPU or memory claim. These results are workload-specific and are not universal performance claims. See the full [checked-in benchmark reports](reports/query/) and the [public performance evidence page](https://cristianbarragan.github.io/Foundgine/performance/#coffeebeanery-performance).

## Benchmark matrix

- PostgreSQL fixture: 1,000 customers, 4,000 relationships, 12,000 contracts, 48,000 transactions.
- Query: top 50 customers with the full `Customer -> Relationship -> Contract -> Transaction` graph.
- Whole-graph mutation: nested graph create.
- Upsert + select: real upsert of existing deterministic customer rows followed by the exact same top-50/full-graph query.
- Concurrency: configurable; the curated baseline uses 1, 8 and 32.
- Mutation/upsert batch sizes: 1, 10 and 50.
- Warm-up: 3 seconds.
- Measurement: 10 seconds.
- Request timeout: 5 seconds.
- Latency: p50, p95, p99.
- Throughput: HTTP RPS and logical/s where batching applies.
- Docker metrics: API-container CPU and memory.

## Important workload semantics

### Query

The standalone query is the canonical read workload. The same full graph is used by the corrected upsert + select workload.

### Mutation

A batch of 50 means one HTTP request represents 50 independent logical mutations. Therefore request RPS and logical/s answer different questions and both are reported.

### Upsert + select

The combined workload is one measured client operation:

![PlantUML diagram: README, diagram 1](assets/readme-plantuml-01.svg)

The stopwatch spans both HTTP calls. The upsert targets existing deterministic customers using `CustomerKey` as the conflict identity. This prevents the workload from degenerating into repeated inserts and ensures the following select reads the same graph shape as the standalone query benchmark.

Older benchmark rows labelled "Upsert + select" that actually used `createCustomer` are historical diagnostics and must not be mixed with the corrected baseline.

## Cache model

The current warm Foundgine configuration caches the **provider execution plan**. It does not cache database results.

That distinction matters:

![PlantUML diagram: README, diagram 2](assets/readme-plantuml-02.svg)

The next cache experiments should add a **result cache** and measure hit rate, hit/miss latency, CPU, memory and PostgreSQL load. A result-cache hit can potentially avoid database execution and much of the downstream materialization cost, which is a fundamentally different optimization from caching the provider plan.

The benchmark should also add a **FASTER-backed cache provider** as a concrete alternative. FASTER is a future experiment, not a performance claim.

## Run

From `src/csharp/benchmarks/CoffeeBeanery.Performance`:

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
.\run-benchmarks.ps1
```

The query, mutation and update pipelines can also be run independently.

The load test accepts:

```text
BENCHMARK_CONCURRENCY=1,8,16,32,64
BENCHMARK_BATCH_SIZES=1,10,50
BENCHMARK_WARMUP_SECONDS=3
BENCHMARK_DURATION_SECONDS=10
```

## Interpreting the numbers

- **RPS** = completed HTTP requests per second.
- **logical/s** = RPS × logical batch size.
- Latency is per HTTP request. A batch-50 request therefore has one latency sample representing all 50 logical operations in that request.
- Docker CPU is expressed as a percentage across logical CPUs, so values above 100% are normal.

Do not compare batch sizes using HTTP RPS alone.

## Where to go next

The next benchmark cycle should answer four questions:

1. How does the corrected real upsert + full-graph refetch compare across providers?
2. At what payload size does result caching become valuable?
3. What is the effect of plan cache + result cache together?
4. Does a FASTER-backed cache change throughput, memory or PostgreSQL pressure enough to justify its complexity?

For the full findings, limitations and proposed experiments, see [the checked-in benchmark reports](reports/query/).

## Current benchmark status — 2026-08-15

The latest checked-in benchmark baseline is the report set generated on 15 August 2026 in [`reports/query/`](reports/query/). The query, whole-graph mutation and corrected upsert + select rows are all included. Docker CPU and memory fields are zero in this report set and must not be interpreted as measured resource comparisons. Performance and PostgreSQL E2E correctness results are tracked separately.


## Public website evidence

The published benchmark evidence is linked from the [Foundgine performance page](https://cristianbarragan.github.io/Foundgine/performance/#coffeebeanery-performance). The site report index is generated from the checked-in files under `reports/` and serves each JSON, CSV and Markdown artifact. To refresh the website assets after producing new reports, run `./publish-site-reports.ps1` from this directory. `run-benchmarks.ps1` also invokes this publisher after all suites complete successfully.
