# CoffeeBeanery / Three API Performance Benchmark

- Generated: `2026-08-15T11:16:01.4067677+00:00`
- Warm-up: `3s`
- Measurement: `10s`
- Request timeout: `5s`
- Readiness timeout: `120s`
- Drain timeout: `30s`
- Concurrency: `1, 8, 16, 32, 64`

## Results

| Operation | Target | Concurrency | Batch | RPS | Logical/s | p50 | p95 | p99 | CPU avg/max | MEM avg/max/end | Errors |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| Query top 50 graph | Hot Chocolate + EF Core | 1 | 1 | 33.80 | 33.80 | 28.43 ms | 53.95 ms | 118.81 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Query top 50 graph | Hot Chocolate + EF Core | 8 | 1 | 152.60 | 152.60 | 44.06 ms | 88.68 ms | 234.32 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Query top 50 graph | Hot Chocolate + EF Core | 16 | 1 | 71.40 | 71.40 | 121.71 ms | 599.97 ms | 982.08 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Query top 50 graph | Hot Chocolate + EF Core | 32 | 1 | 189.70 | 189.70 | 155.86 ms | 222.51 ms | 526.31 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Query top 50 graph | Hot Chocolate + EF Core | 64 | 1 | 152.20 | 152.20 | 389.66 ms | 594.80 ms | 667.88 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Mutation whole graph | Hot Chocolate + EF Core | 1 | 1 | 211.00 | 211.00 | 4.08 ms | 5.58 ms | 6.80 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Mutation whole graph | Hot Chocolate + EF Core | 8 | 1 | 256.80 | 256.80 | 17.03 ms | 83.59 ms | 191.39 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Mutation whole graph | Hot Chocolate + EF Core | 16 | 1 | 319.60 | 319.60 | 25.07 ms | 175.64 ms | 323.63 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Mutation whole graph | Hot Chocolate + EF Core | 32 | 1 | 641.70 | 641.70 | 30.61 ms | 153.74 ms | 239.34 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Mutation whole graph | Hot Chocolate + EF Core | 64 | 1 | 455.60 | 455.60 | 81.17 ms | 449.52 ms | 785.43 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Mutation whole graph | Hot Chocolate + EF Core | 1 | 10 | 40.70 | 407.00 | 22.83 ms | 29.33 ms | 36.24 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Mutation whole graph | Hot Chocolate + EF Core | 8 | 10 | 220.60 | 2206.00 | 34.08 ms | 43.54 ms | 51.35 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Mutation whole graph | Hot Chocolate + EF Core | 16 | 10 | 148.30 | 1483.00 | 88.78 ms | 224.38 ms | 484.41 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Mutation whole graph | Hot Chocolate + EF Core | 32 | 10 | 348.00 | 3480.00 | 86.59 ms | 121.09 ms | 142.62 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Mutation whole graph | Hot Chocolate + EF Core | 64 | 10 | 354.70 | 3547.00 | 168.06 ms | 277.06 ms | 351.40 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Mutation whole graph | Hot Chocolate + EF Core | 1 | 50 | 8.40 | 420.00 | 114.20 ms | 134.09 ms | 186.63 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Mutation whole graph | Hot Chocolate + EF Core | 8 | 50 | 45.70 | 2285.00 | 168.96 ms | 205.25 ms | 244.48 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Mutation whole graph | Hot Chocolate + EF Core | 16 | 50 | 71.60 | 3580.00 | 209.35 ms | 297.66 ms | 356.40 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Mutation whole graph | Hot Chocolate + EF Core | 32 | 50 | 56.40 | 2820.00 | 373.88 ms | 1553.53 ms | 1777.75 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Mutation whole graph | Hot Chocolate + EF Core | 64 | 50 | 63.80 | 3190.00 | 701.45 ms | 3211.90 ms | 3425.43 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Upsert + select (upsert then query top 50 full graph) | Hot Chocolate + EF Core | 1 | 1 | 32.00 | 32.00 | 23.44 ms | 64.94 ms | 129.59 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Upsert + select (upsert then query top 50 full graph) | Hot Chocolate + EF Core | 8 | 1 | 144.40 | 144.40 | 50.90 ms | 82.18 ms | 102.84 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Upsert + select (upsert then query top 50 full graph) | Hot Chocolate + EF Core | 16 | 1 | 174.70 | 174.70 | 86.85 ms | 127.36 ms | 149.74 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Upsert + select (upsert then query top 50 full graph) | Hot Chocolate + EF Core | 32 | 1 | 122.90 | 122.90 | 206.40 ms | 515.17 ms | 1156.21 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Upsert + select (upsert then query top 50 full graph) | Hot Chocolate + EF Core | 64 | 1 | 172.80 | 172.80 | 360.86 ms | 498.37 ms | 562.48 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Upsert + select (upsert then query top 50 full graph) | Hot Chocolate + EF Core | 1 | 10 | 23.30 | 233.00 | 41.22 ms | 47.91 ms | 55.48 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Upsert + select (upsert then query top 50 full graph) | Hot Chocolate + EF Core | 8 | 10 | 92.00 | 920.00 | 82.95 ms | 104.04 ms | 124.16 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Upsert + select (upsert then query top 50 full graph) | Hot Chocolate + EF Core | 16 | 10 | 119.50 | 1195.00 | 128.10 ms | 165.00 ms | 191.12 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Upsert + select (upsert then query top 50 full graph) | Hot Chocolate + EF Core | 32 | 10 | 136.10 | 1361.00 | 228.90 ms | 288.20 ms | 317.95 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Upsert + select (upsert then query top 50 full graph) | Hot Chocolate + EF Core | 64 | 10 | 138.30 | 1383.00 | 456.15 ms | 587.27 ms | 652.43 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Upsert + select (upsert then query top 50 full graph) | Hot Chocolate + EF Core | 1 | 50 | 5.60 | 280.00 | 175.11 ms | 187.66 ms | 224.13 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Upsert + select (upsert then query top 50 full graph) | Hot Chocolate + EF Core | 8 | 50 | 32.10 | 1605.00 | 242.96 ms | 278.20 ms | 300.49 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Upsert + select (upsert then query top 50 full graph) | Hot Chocolate + EF Core | 16 | 50 | 49.90 | 2495.00 | 312.24 ms | 365.84 ms | 393.13 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Upsert + select (upsert then query top 50 full graph) | Hot Chocolate + EF Core | 32 | 50 | 68.20 | 3410.00 | 465.67 ms | 533.47 ms | 577.04 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |
| Upsert + select (upsert then query top 50 full graph) | Hot Chocolate + EF Core | 64 | 50 | 75.00 | 3750.00 | 856.86 ms | 1030.52 ms | 1088.87 ms | 0.00/0.00% | 0.00/0.00/0.00 MB | 0 |

## Workloads

**MutationWholeGraph** creates one Customer, one CustomerBankingRelationship, one Contract and two Transactions in one GraphQL mutation. The child foreign keys are resolved from the parent operation results by Foundgine.

**QueryTop50** selects the first 50 customers and traverses Customer -> CustomerBankingRelationship -> Contract -> Transaction.

The Foundgine warm cache applies to the provider execution plan for the read/query workload. Mutation plans are compiled per request because their parameter values are intentionally dynamic.

Each warm-up and measurement phase has a hard wall-clock boundary. At phase expiry, new requests stop immediately and in-flight HTTP requests are cancelled. Cancelled requests at the phase boundary are not counted as errors or measured samples.

Requests that exceed the explicit request timeout are counted as timeouts. The benchmark no longer uses a 30-second HttpClient timeout to drain a phase.
