# CoffeeBeanery / Three API Performance Benchmark

- Generated: `2026-08-15T05:35:13.1597543+00:00`
- Warm-up: `3s`
- Measurement: `10s`
- Request timeout: `5s`
- Readiness timeout: `120s`
- Drain timeout: `30s`
- Concurrency: `1, 8, 16, 32, 64`

## Results

| Operation | Target | Concurrency | Batch | RPS | Logical/s | p50 | p95 | p99 | CPU avg/max | MEM avg/max/end | Errors |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| Query top 50 graph | Foundgine - no cache | 1 | 1 | 223.60 | 223.60 | 3.91 ms | 6.75 ms | 9.76 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Query top 50 graph | Foundgine - no cache | 8 | 1 | 1234.70 | 1234.70 | 5.65 ms | 10.48 ms | 14.74 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Query top 50 graph | Foundgine - no cache | 16 | 1 | 1599.50 | 1599.50 | 8.87 ms | 16.53 ms | 21.22 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Query top 50 graph | Foundgine - no cache | 32 | 1 | 1786.40 | 1786.40 | 16.13 ms | 30.90 ms | 40.22 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Query top 50 graph | Foundgine - no cache | 64 | 1 | 2019.70 | 2019.70 | 28.48 ms | 53.83 ms | 69.14 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Mutation whole graph | Foundgine - no cache | 1 | 1 | 192.40 | 192.40 | 4.65 ms | 6.73 ms | 8.94 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Mutation whole graph | Foundgine - no cache | 8 | 1 | 590.80 | 590.80 | 13.15 ms | 16.85 ms | 19.66 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Mutation whole graph | Foundgine - no cache | 16 | 1 | 485.00 | 485.00 | 30.08 ms | 45.92 ms | 64.61 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Mutation whole graph | Foundgine - no cache | 32 | 1 | 171.00 | 171.00 | 63.14 ms | 122.47 ms | 205910.97 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Mutation whole graph | Foundgine - no cache | 64 | 1 | 458.70 | 458.70 | 114.73 ms | 258.00 ms | 468.61 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Mutation whole graph | Foundgine - no cache | 1 | 10 | 133.10 | 1331.00 | 6.64 ms | 9.72 ms | 12.70 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Mutation whole graph | Foundgine - no cache | 8 | 10 | 366.00 | 3660.00 | 20.41 ms | 26.31 ms | 32.37 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Mutation whole graph | Foundgine - no cache | 16 | 10 | 375.10 | 3751.00 | 39.68 ms | 54.43 ms | 86.12 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Mutation whole graph | Foundgine - no cache | 32 | 10 | 389.20 | 3892.00 | 77.30 ms | 101.31 ms | 125.86 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Mutation whole graph | Foundgine - no cache | 64 | 10 | 389.50 | 3895.00 | 156.48 ms | 198.04 ms | 227.67 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Mutation whole graph | Foundgine - no cache | 1 | 50 | 15.70 | 785.00 | 73.74 ms | 86.44 ms | 92.78 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Mutation whole graph | Foundgine - no cache | 8 | 50 | 133.50 | 6675.00 | 51.89 ms | 92.91 ms | 124.36 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Mutation whole graph | Foundgine - no cache | 16 | 50 | 142.20 | 7110.00 | 98.97 ms | 164.63 ms | 191.43 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Mutation whole graph | Foundgine - no cache | 32 | 50 | 155.70 | 7785.00 | 195.26 ms | 245.55 ms | 282.81 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Mutation whole graph | Foundgine - no cache | 64 | 50 | 150.70 | 7535.00 | 419.53 ms | 493.81 ms | 552.76 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Upsert + select (upsert then query top 50 full graph) | Foundgine - no cache | 1 | 1 | 80.80 | 80.80 | 9.69 ms | 21.63 ms | 38.14 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Upsert + select (upsert then query top 50 full graph) | Foundgine - no cache | 8 | 1 | 372.30 | 372.30 | 16.76 ms | 44.11 ms | 49.52 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Upsert + select (upsert then query top 50 full graph) | Foundgine - no cache | 16 | 1 | 372.60 | 372.60 | 33.45 ms | 78.27 ms | 126.36 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Upsert + select (upsert then query top 50 full graph) | Foundgine - no cache | 32 | 1 | 267.00 | 267.00 | 89.61 ms | 198.24 ms | 427.29 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Upsert + select (upsert then query top 50 full graph) | Foundgine - no cache | 64 | 1 | 415.80 | 415.80 | 127.68 ms | 301.33 ms | 402.41 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Upsert + select (upsert then query top 50 full graph) | Foundgine - no cache | 1 | 10 | 86.80 | 868.00 | 10.26 ms | 14.77 ms | 20.06 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Upsert + select (upsert then query top 50 full graph) | Foundgine - no cache | 8 | 10 | 314.20 | 3142.00 | 24.13 ms | 31.67 ms | 38.70 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Upsert + select (upsert then query top 50 full graph) | Foundgine - no cache | 16 | 10 | 317.70 | 3177.00 | 47.74 ms | 60.27 ms | 69.09 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Upsert + select (upsert then query top 50 full graph) | Foundgine - no cache | 32 | 10 | 318.30 | 3183.00 | 95.93 ms | 121.56 ms | 129.78 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Upsert + select (upsert then query top 50 full graph) | Foundgine - no cache | 64 | 10 | 326.30 | 3263.00 | 185.26 ms | 256.37 ms | 302.74 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Upsert + select (upsert then query top 50 full graph) | Foundgine - no cache | 1 | 50 | 13.90 | 695.00 | 67.83 ms | 86.77 ms | 101.10 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Upsert + select (upsert then query top 50 full graph) | Foundgine - no cache | 8 | 50 | 113.90 | 5695.00 | 61.92 ms | 80.83 ms | 129.36 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Upsert + select (upsert then query top 50 full graph) | Foundgine - no cache | 16 | 50 | 131.70 | 6585.00 | 111.51 ms | 178.66 ms | 224.44 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Upsert + select (upsert then query top 50 full graph) | Foundgine - no cache | 32 | 50 | 148.00 | 7400.00 | 208.19 ms | 282.71 ms | 309.95 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Upsert + select (upsert then query top 50 full graph) | Foundgine - no cache | 64 | 50 | 148.70 | 7435.00 | 425.45 ms | 550.33 ms | 582.80 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |

## Workloads

**MutationWholeGraph** creates one Customer, one CustomerBankingRelationship, one Contract and two Transactions in one GraphQL mutation. The child foreign keys are resolved from the parent operation results by Foundgine.

**QueryTop50** selects the first 50 customers and traverses Customer -> CustomerBankingRelationship -> Contract -> Transaction.

The Foundgine warm cache applies to the provider execution plan for the read/query workload. Mutation plans are compiled per request because their parameter values are intentionally dynamic.

Each warm-up and measurement phase has a hard wall-clock boundary. At phase expiry, new requests stop immediately and in-flight HTTP requests are cancelled. Cancelled requests at the phase boundary are not counted as errors or measured samples.

Requests that exceed the explicit request timeout are counted as timeouts. The benchmark no longer uses a 30-second HttpClient timeout to drain a phase.
