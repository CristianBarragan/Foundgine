# Run 5 — High-Assurance TransferFunds: EF Core vs Foundgine, fastest path each

Run 5 reuses the Run 4 execution harness shape: fresh PostgreSQL per fixture tier, deterministic fixture seeding, warmups, repeated concurrent batches, latency percentiles, RPS, Docker lifecycle, per-run JSON metadata, and `publish-report.ps1` through the shared report publisher.


## What this benchmark is actually comparing

This is intentionally a **fixed endpoint vs dynamic endpoint** comparison, not a claim that the two sides have identical endpoint capabilities. EF Core exposes a pre-written single-transfer `transfer_funds` MCP tool. Foundgine exposes a dynamic semantic capability that can accept an array of transfer commands and lower them into one set-based PostgreSQL execution. The capability difference is the point of the benchmark: it measures whether a dynamic execution engine can batch logical work without requiring a separate hand-written batch endpoint for every operation.

## EF Core locking disclosure

EF Core does not expose a portable first-class `FOR UPDATE` row-lock API through its normal LINQ abstraction. The high-assurance EF benchmark was therefore explicitly modified to use PostgreSQL raw SQL `SELECT ... FOR UPDATE` reads in deterministic account order, plus the same transaction-scoped advisory idempotency lock used by the PostgreSQL implementation. EF Core still performs the tracked state transition and `SaveChangesAsync()`. The baseline should therefore be described as **EF Core + explicit PostgreSQL locking**, not vanilla out-of-the-box EF Core. This keeps the concurrency contract comparable rather than giving EF Core a weaker locking model.

## Compared implementations

Both sides are served over MCP so the comparison isolates the *execution/persistence stack*, not the transport. Each side is measured at **its own fastest available path** — this is not a single-transfer-vs-single-transfer comparison, it is best-vs-best.

- **MCP + EF Core Postgres** (`MCP.EfCore`, port `4411`) — the `transfer_funds` tool invokes `Foundgine.HighAssurance.EfCore.EfTransferFundsService` against the same `banking` schema and same domain `TransferFundsCommand`, authorization rule and invariants. It uses EF Core change tracking for the mutation while retaining the explicit advisory lock and raw `FOR UPDATE` locking reads. EF Core has no batch tool, so this is measured one transfer per MCP call — its fastest available path.
- **MCP + Foundgine Postgres, UNNEST batch** (`MCP.Foundgine`, port `4412`) — the `transfer_funds_batch` tool invokes `Foundgine.HighAssurance.Postgres.PostgresTransferFundsService` with an array of transfer commands per MCP call. Foundgine acquires transaction-scoped advisory locks for the batch, locks the union of involved accounts in deterministic order, validates the locked snapshot, and executes the state transition/idempotency/audit work in one PostgreSQL statement using `unnest(...)` over typed arrays. This is Foundgine's fastest available path, so it is what's compared — not the single-transfer `transfer_funds` tool.

The runner reports throughput as **operations/second** on both sides (EF Core: 1 op per MCP call; Foundgine: `RUN5_BATCH_SIZE` ops per MCP call, default `64`), and also reports an *amortized* per-operation latency for the Foundgine batch (batch wall time ÷ batch size) alongside EF Core's true per-operation latency, since a raw batch-latency vs single-call-latency comparison would not be apples-to-apples.

## Default matrix

- customers/account pairs: `10,100,1000,10000`
- concurrency: `8,16,32,64`
- runs per tier: `30`
- warmups: `5`
- transfer amount: `1`
- actor: deterministic authorized owner
- tenant: `1`
- unique idempotency key per operation
- Foundgine batch size: `64` (`RUN5_BATCH_SIZE`)

## Run

```powershell
.\run-agent-benchmark.ps1 -CustomerCounts 10,100,1000,10000 -Concurrency 8,16,32,64 -Runs 30 -Warmups 5 -Publish
```

Reports are written under `artifacts/<tier>/concurrency-<n>/` and published using the same shared `publish-report-common.ps1` path used by Run 4.

## Published result snapshot — 10 customers / concurrency 64

The latest 30-run snapshot reports:

| Metric | MCP + EF Core | MCP + Foundgine UNNEST batch |
|---|---:|---:|
| Average throughput | 980.1 ops/s | **10,731.0 ops/s** |
| Throughput delta | — | **+994.8% / 10.95×** |
| Average wall | 45.3 ms / transfer | 180.5 ms / 64-operation batch |
| Effective amortized operation latency | 45.3 ms | **~2.82 ms** |
| p50 | 36.3 ms | 129.5 ms / batch |
| p95 | 118.9 ms | 401.9 ms / batch |
| p99 | 126.8 ms | 520.7 ms / batch |
| Failed | 0 | 0 |

The batch latency and single-transfer latency are deliberately kept separate: Foundgine's one MCP request carries 64 logical operations. The throughput comparison is the primary cross-model metric; the amortized latency is a derived per-operation view.
