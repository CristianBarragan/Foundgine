# Foundgine Samples

There are intentionally only two public Supply Chain samples.

| Sample | Purpose |
|---|---|
| `Foundgine.SupplyChain` | **Starter** — one project, small domain, basic semantic model, authorization, MCP and PostgreSQL. |
| `Foundgine.SupplyChain.Advanced` | **Advanced** — full semantic proving ground with grounding, retrieval, bounded traversal, authorization, adversarial tests and agent-facing E2E execution. |

Do not add a new sample just to demonstrate another capability. If a capability belongs to the advanced Supply Chain story, add it there. If it is framework-wide behavior, add a test under `src/csharp/tests/`. Benchmark-only applications and compatibility fixtures belong under `src/csharp/benchmarks/`.
