# CoffeeBeanery / Three API Performance Benchmark

- Generated: `2026-08-15T11:24:37.4866941+00:00`
- Warm-up: `3s`
- Measurement: `10s`
- Request timeout: `5s`
- Readiness timeout: `120s`
- Drain timeout: `30s`
- Concurrency: `1, 8, 16, 32, 64`

## Results

| Operation | Target | Concurrency | Batch | RPS | Logical/s | p50 | p95 | p99 | CPU avg/max | MEM avg/max/end | Errors |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| Query top 50 graph | Foundgine - no cache | 1 | 1 | 360.10 | 360.10 | 2.56 ms | 3.72 ms | 4.76 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Query top 50 graph | Foundgine - no cache | 8 | 1 | 1914.20 | 1914.20 | 3.79 ms | 6.53 ms | 8.50 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Query top 50 graph | Foundgine - no cache | 16 | 1 | 2017.90 | 2017.90 | 6.94 ms | 13.74 ms | 20.54 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Query top 50 graph | Foundgine - no cache | 32 | 1 | 2427.10 | 2427.10 | 11.86 ms | 23.34 ms | 30.63 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Query top 50 graph | Foundgine - no cache | 64 | 1 | 2587.60 | 2587.60 | 22.37 ms | 43.43 ms | 57.00 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Mutation whole graph | Foundgine - no cache | 1 | 1 | 274.10 | 274.10 | 3.45 ms | 4.20 ms | 4.75 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Mutation whole graph | Foundgine - no cache | 8 | 1 | 802.40 | 802.40 | 10.16 ms | 12.68 ms | 14.33 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Mutation whole graph | Foundgine - no cache | 16 | 1 | 806.30 | 806.30 | 20.22 ms | 24.09 ms | 25.56 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Mutation whole graph | Foundgine - no cache | 32 | 1 | 795.80 | 795.80 | 39.36 ms | 46.63 ms | 56.01 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Mutation whole graph | Foundgine - no cache | 64 | 1 | 810.00 | 810.00 | 77.16 ms | 87.11 ms | 102.88 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Mutation whole graph | Foundgine - no cache | 1 | 10 | 230.90 | 2309.00 | 4.06 ms | 5.11 ms | 6.01 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Mutation whole graph | Foundgine - no cache | 8 | 10 | 572.40 | 5724.00 | 13.33 ms | 15.99 ms | 17.62 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Mutation whole graph | Foundgine - no cache | 16 | 10 | 571.70 | 5717.00 | 26.90 ms | 33.66 ms | 37.82 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Mutation whole graph | Foundgine - no cache | 32 | 10 | 590.40 | 5904.00 | 54.19 ms | 63.64 ms | 73.17 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Mutation whole graph | Foundgine - no cache | 64 | 10 | 561.00 | 5610.00 | 107.77 ms | 133.68 ms | 246.17 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Mutation whole graph | Foundgine - no cache | 1 | 50 | 19.70 | 985.00 | 48.74 ms | 53.46 ms | 55.27 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Mutation whole graph | Foundgine - no cache | 8 | 50 | 153.10 | 7655.00 | 50.71 ms | 55.85 ms | 59.16 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Mutation whole graph | Foundgine - no cache | 16 | 50 | 260.90 | 13045.00 | 58.96 ms | 69.19 ms | 76.13 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Mutation whole graph | Foundgine - no cache | 32 | 50 | 264.40 | 13220.00 | 116.28 ms | 140.18 ms | 169.34 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Mutation whole graph | Foundgine - no cache | 64 | 50 | 261.50 | 13075.00 | 240.53 ms | 281.29 ms | 337.07 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Upsert + select (upsert then query top 50 full graph) | Foundgine - no cache | 1 | 1 | 155.70 | 155.70 | 6.12 ms | 7.36 ms | 8.34 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Upsert + select (upsert then query top 50 full graph) | Foundgine - no cache | 8 | 1 | 649.20 | 649.20 | 11.80 ms | 16.13 ms | 19.21 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Upsert + select (upsert then query top 50 full graph) | Foundgine - no cache | 16 | 1 | 649.00 | 649.00 | 24.32 ms | 31.55 ms | 38.08 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Upsert + select (upsert then query top 50 full graph) | Foundgine - no cache | 32 | 1 | 696.70 | 696.70 | 45.04 ms | 56.17 ms | 65.58 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Upsert + select (upsert then query top 50 full graph) | Foundgine - no cache | 64 | 1 | 650.80 | 650.80 | 95.21 ms | 128.18 ms | 141.59 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Upsert + select (upsert then query top 50 full graph) | Foundgine - no cache | 1 | 10 | 139.90 | 1399.00 | 6.75 ms | 8.46 ms | 10.12 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Upsert + select (upsert then query top 50 full graph) | Foundgine - no cache | 8 | 10 | 467.00 | 4670.00 | 16.66 ms | 20.38 ms | 22.89 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Upsert + select (upsert then query top 50 full graph) | Foundgine - no cache | 16 | 10 | 496.80 | 4968.00 | 31.91 ms | 37.62 ms | 41.46 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Upsert + select (upsert then query top 50 full graph) | Foundgine - no cache | 32 | 10 | 504.10 | 5041.00 | 62.85 ms | 76.43 ms | 85.05 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Upsert + select (upsert then query top 50 full graph) | Foundgine - no cache | 64 | 10 | 510.20 | 5102.00 | 121.93 ms | 158.67 ms | 172.50 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Upsert + select (upsert then query top 50 full graph) | Foundgine - no cache | 1 | 50 | 18.60 | 930.00 | 51.90 ms | 55.95 ms | 56.73 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Upsert + select (upsert then query top 50 full graph) | Foundgine - no cache | 8 | 50 | 142.00 | 7100.00 | 54.63 ms | 60.00 ms | 62.67 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Upsert + select (upsert then query top 50 full graph) | Foundgine - no cache | 16 | 50 | 233.90 | 11695.00 | 66.05 ms | 78.95 ms | 88.24 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Upsert + select (upsert then query top 50 full graph) | Foundgine - no cache | 32 | 50 | 246.00 | 12300.00 | 124.78 ms | 171.19 ms | 192.17 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Upsert + select (upsert then query top 50 full graph) | Foundgine - no cache | 64 | 50 | 245.30 | 12265.00 | 234.33 ms | 379.98 ms | 426.06 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |

## Workloads

**MutationWholeGraph** creates one Customer, one CustomerBankingRelationship, one Contract and two Transactions in one GraphQL mutation. The child foreign keys are resolved from the parent operation results by Foundgine.

**QueryTop50** selects the first 50 customers and traverses Customer -> CustomerBankingRelationship -> Contract -> Transaction.

The Foundgine warm cache applies to the provider execution plan for the read/query workload. Mutation plans are compiled per request because their parameter values are intentionally dynamic.

Each warm-up and measurement phase has a hard wall-clock boundary. At phase expiry, new requests stop immediately and in-flight HTTP requests are cancelled. Cancelled requests at the phase boundary are not counted as errors or measured samples.

Requests that exceed the explicit request timeout are counted as timeouts. The benchmark no longer uses a 30-second HttpClient timeout to drain a phase.
