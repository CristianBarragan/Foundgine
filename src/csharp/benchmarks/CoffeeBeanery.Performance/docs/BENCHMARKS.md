# Benchmark Methodology

The benchmark compares the same PostgreSQL-backed graph workload across:

| Service | Read path | Write path | Cache |
|---|---|---|---|
| Hot Chocolate + EF Core | EF Core `Include` graph | EF Core `SaveChangesAsync` graph insert | EF Core/ASP.NET runtime only |
| Foundgine cold | Semantic model -> authorization -> planner -> SQL compiler -> execution | GraphQL mutation adapter -> nested mutation planner -> SQL mutation compiler -> dependency-aware execution | No provider-plan cache |
| Foundgine warm | Same Foundgine pipeline | Same Foundgine mutation pipeline | Provider execution plan cached for reads |

## Correctness comes first

The loader performs a readiness check and then a correctness preflight for every API. It executes one representative query and one representative mutation and rejects HTTP errors and GraphQL responses containing an `errors` array.

A broken API or workload never stops the remaining matrix. Failed rows are marked diagnostic and are excluded from performance comparisons.

## Measurement

The loader measures completed requests/second and p50/p95/p99 latency at each configured concurrency level. Request timeouts are tracked separately from application errors. A request already in flight when the measurement window expires is allowed to finish; the per-request timeout prevents a broken target from hanging a benchmark run indefinitely.

## Workloads

### Query — top 50

The loader selects the first 50 customers and traverses:

`Customer -> CustomerBankingRelationship -> Contract -> Transaction`

The fixture contains 1,000 customers, 4 relationships per customer, 3 contracts per relationship, and 4 transactions per contract by default.

### Mutation — whole graph create


The loader creates:

`Customer -> CustomerBankingRelationship -> Contract -> 2 Transactions`

Mutation keys are generated dynamically for every request, so the benchmark does not repeatedly upsert or collide with one fixed identity.

## Cache comparison

The Foundgine warm configuration caches only the provider execution plan for the read workload. The corrected upsert workload uses dynamic request values but a stable plan shape; result caching is not currently implemented in the benchmark. A future result-cache experiment should report hit/miss rate and database-resource effects. FASTER is another future cache-provider experiment.

### Upsert + select — write then refetch

The corrected loader performs a real upsert against existing deterministic customer rows using `CustomerKey` as the conflict identity, then immediately executes the exact same `QueryTop50` full graph. Batch sizes represent multiple existing-row upserts in one GraphQL request. One latency sample covers the complete upsert + refetch operation.

The read half is intentionally identical to the standalone query workload, so the combined result can be compared directly with the query baseline.


## Current performance baseline — 2026-08-15

See [`../reports/query/`](../reports/query/) for the latest confirmed results. The current baseline shows a substantial query-performance gap versus Hot Chocolate + EF Core, while Foundgine mutation throughput is considerably stronger. Do not mix historical benchmark runs with the current baseline.
