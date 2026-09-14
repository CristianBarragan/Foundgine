# Benchmark case isolation

The benchmark matrix is designed so one case cannot contaminate the next case.

## Concurrency matrix

The default matrix is:

- `1`
- `8`
- `16`

Override with `BENCHMARK_CONCURRENCY` when deliberately testing another matrix.

## Per-case lifecycle

For every target + workload + concurrency combination:

1. Restore the deterministic 10,000-row-per-graph-table fixture.
2. Wait for the configured case cooldown.
3. Run warm-up.
4. Require every concurrency worker to complete at least one successful workload request and require zero warm-up errors/timeouts.
5. If warm-up fails, record `INVALID_WARMUP`, do not run the measurement, restore the fixture, and continue to the next case.
6. Restore the deterministic fixture again because warm-up may mutate it.
7. Wait for the case cooldown.
8. Run the fixed measurement window.
9. Allow in-flight requests to drain, but never include drain time in RPS.
10. Restore the fixture again before moving to the next case.
11. Wait for the case cooldown.

This makes every measured case start from the same database contents regardless of the preceding workload or target.

## Deterministic fixture

The loader recreates the graph tables using deterministic keys, IDs, values, and a fixed `ProcessedDateTime`. The reset is performed directly in PostgreSQL with `TRUNCATE ... RESTART IDENTITY CASCADE` followed by deterministic inserts.

The benchmark graph is:

`Customer -> CustomerBankingRelationship -> Contract -> Transaction`

with 10,000 rows in each graph table by default. The account table is also restored because contracts and transactions reference it.

## Warm-up is a gate, not a measurement

A warm-up timeout is not silently converted into a performance number. If any worker fails to complete a successful warm-up request, that case is invalid. This prevents output such as `RPS=0.8` with a p99 at the timeout boundary from being presented as a meaningful throughput comparison.

## Measurement clock

The measurement window is fixed to `BENCHMARK_DURATION_SECONDS`. RPS is calculated against that configured window. Requests that started before the deadline are allowed to finish and remain part of the measured sample; requests started after the deadline are drained and excluded.

The benchmark also applies a short backoff after failed requests so a saturated target does not turn repeated timeouts into an artificial retry storm.
