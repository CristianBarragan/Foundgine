# SupplyChain C# arm: preliminary closed-loop results (release 3.4.0)

**Status: preliminary, not claim-grade.** This page records one observed run of the C# pair
(Hot Chocolate + plain EF Core, "baseline", versus Foundgine GraphQL adapter + Foundgine EF Core
provider, "Foundgine") on the `sc-cs-orders-customer` scenario. It does not satisfy the claim-grade
rule in `tools/foundgine-perf/baselines/README.md` (at least five sessions, response equivalence
passed, resource telemetry present), so it must not be quoted as a performance claim for either arm.
Contract and gates: [SUPPLYCHAIN-ARMS.md](SUPPLYCHAIN-ARMS.md).

## Executive summary

- **On this workload, the Foundgine arm delivered lower throughput and higher p99 latency than the
  plain Hot Chocolate + EF Core baseline in every cell where both arms were valid.** At concurrency
  1, 8 and 16 the baseline served roughly 2.3x, 3.1x and 2.4x the requests per second, and its p99 was
  about 1.8x, 2.8x and 1.7x lower.
- **Foundgine's throughput plateaued near 510-520 rps from concurrency 16 to 32**, while the baseline
  peaked at 1,245 rps at concurrency 16. At concurrency 32 the Foundgine cell was valid (522 rps,
  p99 112.5 ms) and the baseline cell was not, so no ranking is made at that level.
- **Both arms were saturated or failed validation from concurrency 64**: 76 rps with p99 7.4 s
  (Foundgine) and 112 rps with p99 4.0 s (baseline). These cells measure overload, not capacity.
  The baseline concurrency 128 cell is also invalid; the Foundgine concurrency 128 cell was not
  captured in the supplied output.
- **What this does and does not show.** It shows a per-request cost gap on one read-heavy query
  (`salesOrder(first: 50)` with lines and shipments, as customer `alice`). It does not show behaviour
  for mutations, other identities, the Java pair, or any resource usage.

## Results

| Concurrency | Foundgine rps | Foundgine p99 (ms) | Foundgine valid | Baseline rps | Baseline p99 (ms) | Baseline valid | Baseline / Foundgine rps |
|---:|---:|---:|:---:|---:|---:|:---:|---:|
| 1 | 111 | 15.8 | yes | 254 | 8.7 | yes | 2.3x |
| 8 | 354 | 44.3 | yes | 1,086 | 16.0 | yes | 3.1x |
| 16 | 509 | 52.9 | yes | 1,245 | 31.8 | yes | 2.4x |
| 32 | 522 | 112.5 | yes | 369 | 154.6 | **no** | not comparable |
| 64 | 76 | 7,369.6 | **no** | 112 | 4,043.2 | **no** | not comparable |
| 128 | not captured | not captured | not captured | 104 | 389.5 | **no** | not comparable |

Workload: closed loop, scenario `sc-cs-orders-customer` (one request: `salesOrder(first: 50)` with
`lines` and `shipments`, as identity `alice`). Each cell restarted its container and truncated the
SupplyChain tables beforehand (the `truncate cascades` notices are expected). Table values are copied
from the harness console output; the harness prints only `valid=True/False`, not the reason.

## Why the results look like this

Only the measurements above are evidence. The explanations below are **hypotheses from the code and
the harness design**, not findings, and each lists how to confirm or reject it.

1. **Per-request semantic work that the baseline does not do.** Foundgine translates the GraphQL
   request into a semantic intent, authorizes it, plans it and compiles a provider plan before EF
   Core executes SQL; the baseline hands the query straight to Hot Chocolate's EF projection. That
   fixed cost per request is the most likely cause of the gap already visible at concurrency 1
   (111 vs 254 rps, 15.8 vs 8.7 ms p99). *Check:* compare per-request CPU time and a profile of the two
   arms at concurrency 1.
2. **Conservative provider-plan cache partition.** 3.4.0 keeps the plan cache partitioned per
   warrant (see CHANGELOG, "Preserved the conservative per-warrant provider-plan cache partition").
   If the harness issues a fresh warrant or identity context per request, every request can miss the
   cache and recompile. *Check:* read plan-cache hit/miss counts during the run; run the same cell
   with a fixed warrant.
3. **Foundgine plateaus earlier, consistent with a CPU-bound service.** Throughput flattening at
   about 510-520 rps from concurrency 16 while p99 doubles (52.9 to 112.5 ms) is the shape of a
   saturated CPU or lock, not of added parallelism. *Check:* resource telemetry for the service
   container (the harness samples it, but the figures were not captured here) and equal cpuset
   allocation for both arms.
4. **Both arms collapse at concurrency 64 or above.** p99 values of 4-7 s with falling throughput are
   what queueing behind a bounded resource looks like (database connection pool, Redis, the
   single-CPU service container, or the Keycloak token path). Because both arms collapse, this
   points to shared infrastructure or the host rather than either implementation. *Check:* pool
   limits, PostgreSQL `max_connections`, and Redis/Keycloak CPU during these cells.
5. **Why cells are `valid=False`.** A cell is invalid for a recorded reason: warm-up not stable,
   error rate above 1%, a Little's Law mismatch above 15%, token refresh failures, or zero telemetry
   samples (`fpbench/runner.py`). Under overload, error-rate and Little's Law failures are expected.
   The baseline concurrency 32 failure (369 rps, below its own concurrency 16 result) is not
   explained by the supplied output. *Check:* open `invalid_reasons` in
   `<outdir>/sc-cs-orders-customer/cs-baseline/closed-c32.json`.
6. **Odd baseline p99 at concurrency 128.** 389.5 ms at 104 rps is far lower than 4,043 ms at 64, which
   suggests most requests failed fast and were excluded from latency. Treat the latency of invalid
   cells as unreliable.

## Limits of this evidence

- One scenario of the six in `configs/supplychain-csharp.json`; the other five (operator, products,
  mixed identity, shipment update, order create) were not run or not captured.
- The configuration calls for five sessions of 20 s; the supplied output shows one value per cell, and
  whether it is a single session or an aggregate is not recorded.
- Not recorded in the supplied output: run date, host and CPU/memory allocation, the commit or build
  under test, resource telemetry, error counts, and whether the schema-parity, authorization-vector
  and response-equivalence gates passed beforehand. The readiness gate requires those before any
  performance measurement is reported.
- The container is restarted before each cell, so the first requests include warm-up effects that the
  harness's stability check may or may not have excluded.

## Before this can become a published claim

1. Pass the schema-parity, authorization-vector and response-equivalence gates and keep their output.
2. Run all six C# scenarios with at least five sessions and equal, recorded CPU sets.
3. Keep resource telemetry and the `invalid_reasons` of every invalid cell.
4. Profile concurrency 1 on both arms and test the per-warrant plan-cache hypothesis before changing
   any claim or the cache design. Fix the cache only with proof that compiled plans contain no
   subject-specific or request-specific values.
5. Repeat the Java pair separately; the two runtimes use different models and are not comparable.
