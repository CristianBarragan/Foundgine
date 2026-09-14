# Benchmark hardening changes — 2026-08-12

This version fixes the benchmark harness so failures in one case do not invalidate or stop the remaining matrix, and makes the fixture reset deterministic.

## Runtime defaults

- Warm-up: 30 seconds
- Measurement: 30 seconds
- Request timeout: 30 seconds
- Readiness timeout: 180 seconds
- Fixture reset timeout: 180 seconds
- Concurrency: 1, 8, 32

These are defaults only; all are environment-variable configurable.

## Measurement correctness

- A completed request is counted only when the complete logical operation succeeds.
- Errors and timeouts are diagnostic samples and are excluded from RPS and latency percentiles.
- Drain time after the fixed measurement window is never included in the RPS denominator.
- A timeout cannot create a fake ~10,000 ms p99 by being added to the successful latency sample.
- Warm-up failures are diagnostic and do not abort the benchmark matrix.
- A fixture reset failure skips only the affected case rather than terminating the whole loader process.

## Deterministic fixture

Every warm-up and measurement case restores the same 10,000-row-per-graph-table fixture. `ProcessedDateTime` uses a fixed timestamp so the reset is byte-for-byte deterministic for the seeded values.

The reset is performed against the dedicated benchmark database. Before truncation, the loader terminates remaining client backends from timed-out benchmark requests so stale PostgreSQL work cannot hold locks and block the next reset indefinitely.

## Database indexes

The EF Core model contains the traversal indexes:

- `CustomerBankingRelationship(CustomerId, Id)`
- `Contract(CustomerBankingRelationshipId, Id)`
- `Transaction(ContractId, Id)`

The existing unique business keys remain indexed for mutation conflict identities.

## Foundgine provider-plan cache

The provider-plan cache now uses a static query-shape key for pagination. `first`, `offset`, and cursor presence are runtime execution values rather than cache-key values. SQL binds pagination through execution-context parameters, so changing `first: 50` to another value does not require recompiling the static provider plan.

Filters, ordering, selected fields, relationships and authorization structure remain part of the cache key. Dynamic filter values are still kept in the key in this version for conservative correctness.

Cache misses use single-flight compilation through `MemoryProviderPlanCache.GetOrAdd`, preventing concurrent compilation stampedes.

## Mutations

The benchmark retains dynamic create mutations and the whole-graph update workload. The update workload changes at least one column in each table of the benchmark graph: Customer, CustomerBankingRelationship, Contract and Transaction.

Foundgine mutation upserts use PostgreSQL `ON CONFLICT ... DO UPDATE` with `IS DISTINCT FROM` guards so unchanged rows avoid physical updates while changed values still update and return correctly.
