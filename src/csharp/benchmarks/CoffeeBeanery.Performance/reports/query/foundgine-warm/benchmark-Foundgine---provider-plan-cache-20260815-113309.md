# CoffeeBeanery / Three API Performance Benchmark

- Generated: `2026-08-15T11:33:09.5003900+00:00`
- Warm-up: `3s`
- Measurement: `10s`
- Request timeout: `5s`
- Readiness timeout: `120s`
- Drain timeout: `30s`
- Concurrency: `1, 8, 16, 32, 64`

## Results

| Operation | Target | Concurrency | Batch | RPS | Logical/s | p50 | p95 | p99 | CPU avg/max | MEM avg/max/end | Errors |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| Query top 50 graph | Foundgine - provider-plan cache | 1 | 1 | 425.20 | 425.20 | 2.12 ms | 3.17 ms | 3.85 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Query top 50 graph | Foundgine - provider-plan cache | 8 | 1 | 2147.40 | 2147.40 | 3.42 ms | 5.47 ms | 6.88 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Query top 50 graph | Foundgine - provider-plan cache | 16 | 1 | 2600.50 | 2600.50 | 5.60 ms | 10.07 ms | 12.89 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Query top 50 graph | Foundgine - provider-plan cache | 32 | 1 | 2860.50 | 2860.50 | 10.23 ms | 19.13 ms | 24.21 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Query top 50 graph | Foundgine - provider-plan cache | 64 | 1 | 2907.30 | 2907.30 | 19.98 ms | 38.86 ms | 50.07 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Mutation whole graph | Foundgine - provider-plan cache | 1 | 1 | 271.40 | 271.40 | 3.50 ms | 4.19 ms | 4.74 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Mutation whole graph | Foundgine - provider-plan cache | 8 | 1 | 792.80 | 792.80 | 10.20 ms | 12.87 ms | 14.22 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Mutation whole graph | Foundgine - provider-plan cache | 16 | 1 | 796.80 | 796.80 | 20.65 ms | 26.19 ms | 29.03 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Mutation whole graph | Foundgine - provider-plan cache | 32 | 1 | 822.00 | 822.00 | 38.50 ms | 44.06 ms | 50.94 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Mutation whole graph | Foundgine - provider-plan cache | 64 | 1 | 830.50 | 830.50 | 76.23 ms | 83.69 ms | 88.62 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Mutation whole graph | Foundgine - provider-plan cache | 1 | 10 | 232.20 | 2322.00 | 4.06 ms | 4.90 ms | 5.76 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Mutation whole graph | Foundgine - provider-plan cache | 8 | 10 | 566.90 | 5669.00 | 13.51 ms | 15.75 ms | 17.47 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Mutation whole graph | Foundgine - provider-plan cache | 16 | 10 | 603.00 | 6030.00 | 25.86 ms | 29.80 ms | 32.02 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Mutation whole graph | Foundgine - provider-plan cache | 32 | 10 | 606.70 | 6067.00 | 53.05 ms | 60.17 ms | 63.79 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Mutation whole graph | Foundgine - provider-plan cache | 64 | 10 | 598.40 | 5984.00 | 105.81 ms | 119.08 ms | 132.60 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Mutation whole graph | Foundgine - provider-plan cache | 1 | 50 | 19.60 | 980.00 | 49.00 ms | 54.20 ms | 55.76 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Mutation whole graph | Foundgine - provider-plan cache | 8 | 50 | 153.70 | 7685.00 | 50.54 ms | 55.62 ms | 58.43 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Mutation whole graph | Foundgine - provider-plan cache | 16 | 50 | 269.80 | 13490.00 | 57.10 ms | 67.81 ms | 76.95 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Mutation whole graph | Foundgine - provider-plan cache | 32 | 50 | 276.40 | 13820.00 | 111.90 ms | 132.85 ms | 150.42 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Mutation whole graph | Foundgine - provider-plan cache | 64 | 50 | 279.30 | 13965.00 | 225.09 ms | 257.19 ms | 275.89 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Upsert + select (upsert then query top 50 full graph) | Foundgine - provider-plan cache | 1 | 1 | 157.40 | 157.40 | 6.09 ms | 7.22 ms | 8.04 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Upsert + select (upsert then query top 50 full graph) | Foundgine - provider-plan cache | 8 | 1 | 680.10 | 680.10 | 11.33 ms | 14.93 ms | 17.57 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Upsert + select (upsert then query top 50 full graph) | Foundgine - provider-plan cache | 16 | 1 | 688.80 | 688.80 | 22.84 ms | 28.81 ms | 31.94 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Upsert + select (upsert then query top 50 full graph) | Foundgine - provider-plan cache | 32 | 1 | 703.00 | 703.00 | 44.99 ms | 53.93 ms | 58.45 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Upsert + select (upsert then query top 50 full graph) | Foundgine - provider-plan cache | 64 | 1 | 657.70 | 657.70 | 94.13 ms | 121.54 ms | 139.02 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Upsert + select (upsert then query top 50 full graph) | Foundgine - provider-plan cache | 1 | 10 | 140.50 | 1405.00 | 6.73 ms | 8.49 ms | 9.84 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Upsert + select (upsert then query top 50 full graph) | Foundgine - provider-plan cache | 8 | 10 | 490.60 | 4906.00 | 15.89 ms | 19.55 ms | 22.40 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Upsert + select (upsert then query top 50 full graph) | Foundgine - provider-plan cache | 16 | 10 | 487.40 | 4874.00 | 32.35 ms | 40.24 ms | 45.69 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Upsert + select (upsert then query top 50 full graph) | Foundgine - provider-plan cache | 32 | 10 | 503.50 | 5035.00 | 62.44 ms | 78.84 ms | 90.25 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Upsert + select (upsert then query top 50 full graph) | Foundgine - provider-plan cache | 64 | 10 | 507.20 | 5072.00 | 121.61 ms | 164.16 ms | 182.60 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Upsert + select (upsert then query top 50 full graph) | Foundgine - provider-plan cache | 1 | 50 | 18.50 | 925.00 | 52.17 ms | 56.53 ms | 60.31 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Upsert + select (upsert then query top 50 full graph) | Foundgine - provider-plan cache | 8 | 50 | 141.70 | 7085.00 | 54.96 ms | 60.71 ms | 64.41 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Upsert + select (upsert then query top 50 full graph) | Foundgine - provider-plan cache | 16 | 50 | 234.80 | 11740.00 | 65.37 ms | 78.92 ms | 89.14 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Upsert + select (upsert then query top 50 full graph) | Foundgine - provider-plan cache | 32 | 50 | 248.20 | 12410.00 | 124.83 ms | 169.15 ms | 186.15 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Upsert + select (upsert then query top 50 full graph) | Foundgine - provider-plan cache | 64 | 50 | 234.70 | 11735.00 | 248.00 ms | 394.98 ms | 458.02 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |

## Workloads

**MutationWholeGraph** creates one Customer, one CustomerBankingRelationship, one Contract and two Transactions in one GraphQL mutation. The child foreign keys are resolved from the parent operation results by Foundgine.

**QueryTop50** selects the first 50 customers and traverses Customer -> CustomerBankingRelationship -> Contract -> Transaction.

The Foundgine warm cache applies to the provider execution plan for the read/query workload. Mutation plans are compiled per request because their parameter values are intentionally dynamic.

Each warm-up and measurement phase has a hard wall-clock boundary. At phase expiry, new requests stop immediately and in-flight HTTP requests are cancelled. Cancelled requests at the phase boundary are not counted as errors or measured samples.

Requests that exceed the explicit request timeout are counted as timeouts. The benchmark no longer uses a 30-second HttpClient timeout to drain a phase.
