# CoffeeBeanery / Three API Performance Benchmark

- Generated: `2026-08-15T05:22:23.1018393+00:00`
- Warm-up: `3s`
- Measurement: `10s`
- Request timeout: `5s`
- Readiness timeout: `120s`
- Drain timeout: `30s`
- Concurrency: `1, 8, 16, 32, 64`

## Results

| Operation | Target | Concurrency | Batch | RPS | Logical/s | p50 | p95 | p99 | CPU avg/max | MEM avg/max/end | Errors |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| Query top 50 graph | Hot Chocolate + EF Core | 1 | 1 | 30.00 | 30.00 | 30.65 ms | 43.75 ms | 59.77 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Query top 50 graph | Hot Chocolate + EF Core | 8 | 1 | 145.60 | 145.60 | 51.79 ms | 74.03 ms | 88.05 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Query top 50 graph | Hot Chocolate + EF Core | 16 | 1 | 153.40 | 153.40 | 96.42 ms | 154.56 ms | 192.31 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Query top 50 graph | Hot Chocolate + EF Core | 32 | 1 | 154.00 | 154.00 | 199.72 ms | 263.33 ms | 298.72 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Query top 50 graph | Hot Chocolate + EF Core | 64 | 1 | 144.20 | 144.20 | 432.47 ms | 573.78 ms | 649.03 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Mutation whole graph | Hot Chocolate + EF Core | 1 | 1 | 178.80 | 178.80 | 4.92 ms | 7.60 ms | 10.61 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Mutation whole graph | Hot Chocolate + EF Core | 8 | 1 | 653.90 | 653.90 | 8.63 ms | 28.53 ms | 42.33 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Mutation whole graph | Hot Chocolate + EF Core | 16 | 1 | 591.60 | 591.60 | 15.95 ms | 79.81 ms | 120.18 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Mutation whole graph | Hot Chocolate + EF Core | 32 | 1 | 574.10 | 574.10 | 34.03 ms | 168.58 ms | 259.77 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Mutation whole graph | Hot Chocolate + EF Core | 64 | 1 | 585.70 | 585.70 | 72.12 ms | 321.48 ms | 474.06 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Mutation whole graph | Hot Chocolate + EF Core | 1 | 10 | 27.50 | 275.00 | 33.52 ms | 44.12 ms | 57.35 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Mutation whole graph | Hot Chocolate + EF Core | 8 | 10 | 163.50 | 1635.00 | 45.82 ms | 59.97 ms | 71.25 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Mutation whole graph | Hot Chocolate + EF Core | 16 | 10 | 243.60 | 2436.00 | 61.34 ms | 78.40 ms | 90.35 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Mutation whole graph | Hot Chocolate + EF Core | 32 | 10 | 267.80 | 2678.00 | 110.27 ms | 159.92 ms | 195.09 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Mutation whole graph | Hot Chocolate + EF Core | 64 | 10 | 262.60 | 2626.00 | 221.66 ms | 379.39 ms | 491.30 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Mutation whole graph | Hot Chocolate + EF Core | 1 | 50 | 5.70 | 285.00 | 165.47 ms | 207.81 ms | 256.76 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Mutation whole graph | Hot Chocolate + EF Core | 8 | 50 | 34.70 | 1735.00 | 214.87 ms | 268.92 ms | 290.61 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Mutation whole graph | Hot Chocolate + EF Core | 16 | 50 | 41.30 | 2065.00 | 310.99 ms | 724.51 ms | 1666.48 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Mutation whole graph | Hot Chocolate + EF Core | 32 | 50 | 32.60 | 1630.00 | 979.02 ms | 1548.34 ms | 1656.63 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Mutation whole graph | Hot Chocolate + EF Core | 64 | 50 | 74.80 | 3740.00 | 824.42 ms | 1090.34 ms | 1181.77 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Upsert + select (upsert then query top 50 full graph) | Hot Chocolate + EF Core | 1 | 1 | 25.50 | 25.50 | 36.30 ms | 55.07 ms | 61.19 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Upsert + select (upsert then query top 50 full graph) | Hot Chocolate + EF Core | 8 | 1 | 120.20 | 120.20 | 62.25 ms | 92.67 ms | 108.43 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Upsert + select (upsert then query top 50 full graph) | Hot Chocolate + EF Core | 16 | 1 | 140.80 | 140.80 | 104.71 ms | 163.69 ms | 195.45 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Upsert + select (upsert then query top 50 full graph) | Hot Chocolate + EF Core | 32 | 1 | 140.40 | 140.40 | 211.96 ms | 323.15 ms | 388.39 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Upsert + select (upsert then query top 50 full graph) | Hot Chocolate + EF Core | 64 | 1 | 145.80 | 145.80 | 427.94 ms | 540.16 ms | 617.19 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Upsert + select (upsert then query top 50 full graph) | Hot Chocolate + EF Core | 1 | 10 | 13.60 | 136.00 | 67.38 ms | 90.06 ms | 109.78 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Upsert + select (upsert then query top 50 full graph) | Hot Chocolate + EF Core | 8 | 10 | 61.70 | 617.00 | 116.47 ms | 195.63 ms | 255.50 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Upsert + select (upsert then query top 50 full graph) | Hot Chocolate + EF Core | 16 | 10 | 88.40 | 884.00 | 166.97 ms | 241.05 ms | 298.85 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Upsert + select (upsert then query top 50 full graph) | Hot Chocolate + EF Core | 32 | 10 | 100.70 | 1007.00 | 299.45 ms | 420.25 ms | 560.36 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Upsert + select (upsert then query top 50 full graph) | Hot Chocolate + EF Core | 64 | 10 | 104.20 | 1042.00 | 596.78 ms | 816.05 ms | 907.95 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Upsert + select (upsert then query top 50 full graph) | Hot Chocolate + EF Core | 1 | 50 | 4.30 | 215.00 | 224.42 ms | 247.71 ms | 270.65 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Upsert + select (upsert then query top 50 full graph) | Hot Chocolate + EF Core | 8 | 50 | 25.30 | 1265.00 | 306.78 ms | 353.11 ms | 379.70 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Upsert + select (upsert then query top 50 full graph) | Hot Chocolate + EF Core | 16 | 50 | 36.90 | 1845.00 | 410.58 ms | 509.37 ms | 570.30 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Upsert + select (upsert then query top 50 full graph) | Hot Chocolate + EF Core | 32 | 50 | 49.10 | 2455.00 | 634.12 ms | 771.23 ms | 853.81 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Upsert + select (upsert then query top 50 full graph) | Hot Chocolate + EF Core | 64 | 50 | 56.10 | 2805.00 | 1138.24 ms | 1383.99 ms | 1477.17 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |

## Workloads

**MutationWholeGraph** creates one Customer, one CustomerBankingRelationship, one Contract and two Transactions in one GraphQL mutation. The child foreign keys are resolved from the parent operation results by Foundgine.

**QueryTop50** selects the first 50 customers and traverses Customer -> CustomerBankingRelationship -> Contract -> Transaction.

The Foundgine warm cache applies to the provider execution plan for the read/query workload. Mutation plans are compiled per request because their parameter values are intentionally dynamic.

Each warm-up and measurement phase has a hard wall-clock boundary. At phase expiry, new requests stop immediately and in-flight HTTP requests are cancelled. Cancelled requests at the phase boundary are not counted as errors or measured samples.

Requests that exceed the explicit request timeout are counted as timeouts. The benchmark no longer uses a 30-second HttpClient timeout to drain a phase.
