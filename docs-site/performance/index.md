# Performance and benchmark evidence

Foundgine benchmark claims are scoped to explicit workloads. The site distinguishes measured tool calls, timing, RPS and correctness counts from estimated context metrics.

## Agent-work and semantic execution evidence

- [Agent benchmark explorer](../agent-benchmark/index.html)
- [Supply Chain E2E](../agent-benchmark/supply-chain/index.html)
- [Run 5 — high-assurance mutation](../agent-benchmark/run-5/index.html)
- [Run 5b — individual versus batch](../agent-benchmark/run-5b/index.html)

## CoffeeBeanery API and database performance

The separate [CoffeeBeanery benchmark suite](https://github.com/CristianBarragan/Foundgine/tree/main/src/csharp/benchmarks/CoffeeBeanery.Performance) compares Hot Chocolate + EF Core with Foundgine against the same PostgreSQL-backed graph fixture. The [methodology and findings](https://github.com/CristianBarragan/Foundgine/blob/main/src/csharp/benchmarks/CoffeeBeanery.Performance/README.md) describe query, mutation and update workloads. The checked-in report set dated 15 August 2026 includes query, whole-graph mutation and upsert + select measurements. Separate update-suite artifacts are not present in this snapshot. The report JSON records zero for Docker CPU and memory, so it does not support comparative resource-usage claims.

Published JSON, CSV and Markdown report artifacts are indexed at [CoffeeBeanery report index](../assets/coffee-beanery-benchmark/reports-index.json).

## SupplyChain ORM comparison (implementation in progress)

The four-arm [SupplyChain benchmark contract](../../docs/performance/SUPPLYCHAIN-ARMS.md) compares Hot Chocolate/EF Core with Foundgine/EF Core and Spring GraphQL/JPA with Foundgine/JPA. Each pair shares Redis-backed revocation checks and a PostgreSQL durable epoch anchor; the plain Hot Chocolate baseline uses direct StackExchange.Redis/Npgsql rather than Foundgine providers. The implementation and wiring are present, but no accepted runtime measurements are published yet. See `tools/foundgine-perf/docs/SUPPLYCHAIN-ARMS.md` for the run gates.

## Interpretation

Read the experiment question, workload, fixture, concurrency and measurement date before comparing values. Do not generalize beyond the published fixture and configuration. Historical results are not a live feed.

## Continue

[Samples](../samples/index.html) → [Architecture](../architecture/index.html)
