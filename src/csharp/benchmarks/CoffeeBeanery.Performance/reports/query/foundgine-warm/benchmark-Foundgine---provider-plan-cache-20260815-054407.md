# CoffeeBeanery / Three API Performance Benchmark

- Generated: `2026-08-15T05:44:07.9761752+00:00`
- Warm-up: `3s`
- Measurement: `10s`
- Request timeout: `5s`
- Readiness timeout: `120s`
- Drain timeout: `30s`
- Concurrency: `1, 8, 16, 32, 64`

## Results

| Operation | Target | Concurrency | Batch | RPS | Logical/s | p50 | p95 | p99 | CPU avg/max | MEM avg/max/end | Errors |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| Query top 50 graph | Foundgine - provider-plan cache | 1 | 1 | 252.00 | 252.00 | 3.46 ms | 5.93 ms | 8.89 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Query top 50 graph | Foundgine - provider-plan cache | 8 | 1 | 1289.20 | 1289.20 | 5.24 ms | 9.65 ms | 14.16 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Query top 50 graph | Foundgine - provider-plan cache | 16 | 1 | 1619.30 | 1619.30 | 8.67 ms | 17.21 ms | 22.54 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Query top 50 graph | Foundgine - provider-plan cache | 32 | 1 | 1858.40 | 1858.40 | 15.12 ms | 29.17 ms | 37.07 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Query top 50 graph | Foundgine - provider-plan cache | 64 | 1 | 2096.30 | 2096.30 | 27.44 ms | 52.52 ms | 67.86 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Mutation whole graph | Foundgine - provider-plan cache | 1 | 1 | 176.70 | 176.70 | 4.99 ms | 7.32 ms | 11.12 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Mutation whole graph | Foundgine - provider-plan cache | 8 | 1 | 575.60 | 575.60 | 13.22 ms | 17.63 ms | 20.53 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Mutation whole graph | Foundgine - provider-plan cache | 16 | 1 | 557.60 | 557.60 | 27.14 ms | 35.66 ms | 46.24 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Mutation whole graph | Foundgine - provider-plan cache | 32 | 1 | 563.90 | 563.90 | 51.30 ms | 64.66 ms | 77.99 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Mutation whole graph | Foundgine - provider-plan cache | 64 | 1 | 546.00 | 546.00 | 106.61 ms | 141.22 ms | 157.89 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Mutation whole graph | Foundgine - provider-plan cache | 1 | 10 | 134.10 | 1341.00 | 6.56 ms | 9.50 ms | 13.35 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Mutation whole graph | Foundgine - provider-plan cache | 8 | 10 | 367.90 | 3679.00 | 20.25 ms | 26.36 ms | 30.22 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Mutation whole graph | Foundgine - provider-plan cache | 16 | 10 | 395.70 | 3957.00 | 38.05 ms | 49.42 ms | 57.31 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Mutation whole graph | Foundgine - provider-plan cache | 32 | 10 | 396.40 | 3964.00 | 74.39 ms | 94.61 ms | 104.42 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Mutation whole graph | Foundgine - provider-plan cache | 64 | 10 | 390.10 | 3901.00 | 149.40 ms | 185.59 ms | 206.30 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Mutation whole graph | Foundgine - provider-plan cache | 1 | 50 | 15.90 | 795.00 | 58.85 ms | 73.73 ms | 122.57 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Mutation whole graph | Foundgine - provider-plan cache | 8 | 50 | 131.80 | 6590.00 | 56.59 ms | 72.09 ms | 83.28 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Mutation whole graph | Foundgine - provider-plan cache | 16 | 50 | 149.50 | 7475.00 | 97.06 ms | 123.12 ms | 135.76 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Mutation whole graph | Foundgine - provider-plan cache | 32 | 50 | 157.30 | 7865.00 | 192.92 ms | 232.17 ms | 290.33 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Mutation whole graph | Foundgine - provider-plan cache | 64 | 50 | 158.10 | 7905.00 | 395.53 ms | 470.69 ms | 531.82 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Upsert + select (upsert then query top 50 full graph) | Foundgine - provider-plan cache | 1 | 1 | 99.00 | 99.00 | 9.01 ms | 13.39 ms | 19.40 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Upsert + select (upsert then query top 50 full graph) | Foundgine - provider-plan cache | 8 | 1 | 472.00 | 472.00 | 15.73 ms | 22.24 ms | 28.50 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Upsert + select (upsert then query top 50 full graph) | Foundgine - provider-plan cache | 16 | 1 | 493.10 | 493.10 | 31.20 ms | 42.00 ms | 49.13 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Upsert + select (upsert then query top 50 full graph) | Foundgine - provider-plan cache | 32 | 1 | 462.00 | 462.00 | 64.13 ms | 91.27 ms | 109.74 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Upsert + select (upsert then query top 50 full graph) | Foundgine - provider-plan cache | 64 | 1 | 491.20 | 491.20 | 121.37 ms | 164.59 ms | 221.04 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Upsert + select (upsert then query top 50 full graph) | Foundgine - provider-plan cache | 1 | 10 | 81.80 | 818.00 | 10.73 ms | 16.15 ms | 21.30 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Upsert + select (upsert then query top 50 full graph) | Foundgine - provider-plan cache | 8 | 10 | 301.40 | 3014.00 | 24.32 ms | 34.25 ms | 40.97 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Upsert + select (upsert then query top 50 full graph) | Foundgine - provider-plan cache | 16 | 10 | 318.50 | 3185.00 | 46.68 ms | 60.56 ms | 71.16 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Upsert + select (upsert then query top 50 full graph) | Foundgine - provider-plan cache | 32 | 10 | 318.60 | 3186.00 | 97.37 ms | 119.65 ms | 131.10 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Upsert + select (upsert then query top 50 full graph) | Foundgine - provider-plan cache | 64 | 10 | 318.00 | 3180.00 | 193.97 ms | 230.47 ms | 249.54 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Upsert + select (upsert then query top 50 full graph) | Foundgine - provider-plan cache | 1 | 50 | 15.30 | 765.00 | 60.60 ms | 77.00 ms | 90.37 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Upsert + select (upsert then query top 50 full graph) | Foundgine - provider-plan cache | 8 | 50 | 116.60 | 5830.00 | 63.55 ms | 84.06 ms | 105.81 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Upsert + select (upsert then query top 50 full graph) | Foundgine - provider-plan cache | 16 | 50 | 136.00 | 6800.00 | 108.15 ms | 137.81 ms | 154.67 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Upsert + select (upsert then query top 50 full graph) | Foundgine - provider-plan cache | 32 | 50 | 145.80 | 7290.00 | 210.66 ms | 275.20 ms | 308.69 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Upsert + select (upsert then query top 50 full graph) | Foundgine - provider-plan cache | 64 | 50 | 147.50 | 7375.00 | 389.21 ms | 642.08 ms | 817.41 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |

## Workloads

**MutationWholeGraph** creates one Customer, one CustomerBankingRelationship, one Contract and two Transactions in one GraphQL mutation. The child foreign keys are resolved from the parent operation results by Foundgine.

**QueryTop50** selects the first 50 customers and traverses Customer -> CustomerBankingRelationship -> Contract -> Transaction.

The Foundgine warm cache applies to the provider execution plan for the read/query workload. Mutation plans are compiled per request because their parameter values are intentionally dynamic.

Each warm-up and measurement phase has a hard wall-clock boundary. At phase expiry, new requests stop immediately and in-flight HTTP requests are cancelled. Cancelled requests at the phase boundary are not counted as errors or measured samples.

Requests that exceed the explicit request timeout are counted as timeouts. The benchmark no longer uses a 30-second HttpClient timeout to drain a phase.
