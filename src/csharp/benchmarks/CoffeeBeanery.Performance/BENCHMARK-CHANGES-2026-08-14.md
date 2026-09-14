# Benchmark changes — 2026-08-14

## Corrected upsert + select workload

The previous workload labelled `Upsert + select` actually issued `createCustomer` and then queried the top-50 graph. That was not a true upsert workload.

The benchmark now:

1. performs a real `upsertCustomer` using `CustomerKey` as the conflict identity;
2. targets deterministic existing customer rows, so the operation exercises the update side of PostgreSQL upsert rather than becoming an insert-only test;
3. supports logical batch sizes 1, 10 and 50;
4. immediately executes the exact same `customer(first: 50)` full relationship graph used by the standalone query benchmark; and
5. measures the complete write-then-refetch path with one latency stopwatch.

Hot Chocolate + EF Core now has a matching PostgreSQL `ON CONFLICT (CustomerKey)` implementation for the benchmark API, so the corrected workload can be compared across both implementations.

## Reporting change

The repository now documents:

- the query performance finding;
- the mutation/resource-efficiency finding;
- the corrected upsert + select methodology;
- provider-plan cache behaviour;
- the future result-cache experiment; and
- the future FASTER cache-provider experiment.

The older create-then-select rows remain in the repository as historical benchmark artifacts, but they must not be used as the new upsert baseline.


## Current results — 2026-08-15 (corrected)

An earlier version of this note, and the 2026-08-15 report it pointed to, claimed query performance had
fallen behind Hot Chocolate + EF Core. That claim was wrong — it had no raw data checked in to back it
and contradicted every other run on record. It has been retracted; see the correction notice at the top
of [`reports/query/`](reports/query/).

The corrected 2026-08-15 baseline, rebuilt directly from the benchmark log, matches the 2026-08-13
baseline: Foundgine remains substantially faster than Hot Chocolate + EF Core on the query workload at
every tested concurrency, and pulls further ahead on batched (10/50) whole-graph mutation and
upsert+select throughput.
