# Performance and benchmark evidence

Foundgine benchmark claims are scoped to explicit workloads. The site distinguishes measured tool calls, timing, RPS and correctness counts from estimated context metrics.

## Agent-work and semantic execution evidence

- [Agent benchmark explorer](../agent-benchmark/index.html)
- [Supply Chain E2E](../agent-benchmark/supply-chain/index.html)
- [Run 5 — high-assurance mutation](../agent-benchmark/run-5/index.html)
- [Run 5b — individual versus batch](../agent-benchmark/run-5b/index.html)

## CoffeeBeanery API and database performance

The separate [CoffeeBeanery benchmark suite](https://github.com/Foundgine/Foundgine/tree/main/src/csharp/benchmarks/CoffeeBeanery.Performance) compares Hot Chocolate + EF Core with Foundgine against the same PostgreSQL-backed graph fixture. The [methodology and findings](https://github.com/Foundgine/Foundgine/blob/main/src/csharp/benchmarks/CoffeeBeanery.Performance/README.md) describe query, mutation and update workloads. The checked-in report set dated 15 August 2026 includes query, whole-graph mutation and upsert + select measurements. Separate update-suite artifacts are not present in this snapshot. The report JSON records zero for Docker CPU and memory, so it does not support comparative resource-usage claims.

Published JSON, CSV and Markdown report artifacts are indexed at [CoffeeBeanery report index](../assets/coffee-beanery-benchmark/reports-index.json).

## SupplyChain ORM comparison (preliminary C# results)

The four-arm [SupplyChain benchmark contract](../../docs/performance/SUPPLYCHAIN-ARMS.md) compares Hot Chocolate/EF Core with Foundgine/EF Core and Spring GraphQL/JPA with Foundgine/JPA. Each pair shares Redis-backed revocation checks and a PostgreSQL durable epoch anchor; the plain Hot Chocolate baseline uses direct StackExchange.Redis/Npgsql rather than Foundgine providers.

A first, **preliminary** C# closed-loop run on one read scenario (`salesOrder(first: 50)` as a customer) is recorded in [SupplyChain C# preliminary results](../../docs/performance/SUPPLYCHAIN-CSHARP-PRELIMINARY-RESULTS.md). In every cell where both arms were valid (concurrency 1, 8 and 16), the plain Hot Chocolate + EF Core baseline had higher throughput (about 2.3x to 3.1x) and lower p99 than the Foundgine arm. Foundgine plateaued near 510-520 rps; both arms were invalid from concurrency 64. The page lists likely causes as hypotheses with how to check each. The run is not claim-grade (not five sessions, no recorded equivalence gate or resource telemetry), so it is not a published performance claim, and no Java results exist. See `tools/foundgine-perf/docs/SUPPLYCHAIN-ARMS.md` for the run gates.

## Interpretation

Read the experiment question, workload, fixture, concurrency and measurement date before comparing values. Do not generalize beyond the published fixture and configuration. Historical results are not a live feed.

## Continue

[Samples](../samples/index.html) → [Architecture](../architecture/index.html)
