# Run 4 — MCP + Foundgine vs Hot Chocolate + EF Core

Run 4 is the protocol/agent comparison in the Agent End-to-End suite.

## Same load profile as Run 2

Run 4 deliberately uses the identical scale/concurrency matrix as Run 2:

- Customers: **10, 100, 1,000, 10,000**
- Concurrency: **8, 16, 32, 64**
- Runs per tier: **30**
- Warmups: **5**
- Relationships/customer: **4**
- Contracts/relationship: **3**
- Transactions/contract: **4**

At C > customer count, customer IDs are reused round-robin, matching Run 2's load model.

## Flows

1. **GraphQL + Hot Chocolate + EF Core** — every conventional-agent operation crosses the real GraphQL HTTP API backed by EF Core and PostgreSQL. The agent flow performs six GraphQL tool calls.
2. **MCP + Foundgine** — the semantic agent flow performs one `foundgine_query` MCP tool call with the complete provider-neutral graph intent.

Both flows operate on the same PostgreSQL fixture and the same customer-exposure scenario.

## Measurements

### Agent mode

Six GraphQL + Hot Chocolate + EF Core tool calls vs one MCP + Foundgine semantic tool call.

### Protocol mode

One full-graph GraphQL request vs one full-graph MCP request, isolating protocol/runtime overhead from tool-count differences.

## Reliability

Each concurrency cell has independent warmups and measured runs. The benchmark uses a shared pooled `HttpClient`, bounded connection pooling, transient HTTP retry, concurrent-worker error accounting, and p50/p95/p99 reporting. A failed worker is recorded rather than immediately terminating the measurement batch.

Docker metrics are collected separately for PostgreSQL, Hot Chocolate + EF Core, and MCP + Foundgine for every customer/concurrency cell.

Replay mode does not invoke an LLM. Token estimates are heuristic character/4 estimates; they are not provider billing measurements.

## Run

Smoke test:

```powershell
.\run-run4.ps1 -Mode both -RunsPerTier 2,2,2,2 -Warmups 1
```

Full benchmark:

```powershell
.\run-run4.ps1 -Mode both -RunsPerTier 30,30,30,30 -Warmups 5 -Publish
```
