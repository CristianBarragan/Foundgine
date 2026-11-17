# Warrant revocation checkpoint 8 — PostgreSQL durable epoch anchor

## Objective

Checkpoint 8 supplies the first concrete durable implementation of the checkpoint 7
`ISecurityWarrantRevocationEpochAnchor` contract. Redis remains the high-volume revocation
lookup and record store; PostgreSQL is an independent durable high-water authority.

## Implementation

- Added `PostgresSecurityWarrantRevocationEpochAnchor` in `Foundgine.Providers`.
- Added an idempotent, advisory-lock-protected schema migrator.
- Stored one singleton `bigint` high-water mark with a non-negative constraint.
- Advanced it using one atomic `INSERT ... ON CONFLICT ... GREATEST(...) RETURNING` statement.
- Forced `synchronous_commit = on` within each advance transaction and returned only after commit.
- Read directly from the configured `NpgsqlDataSource`; it must target the writable primary, not a read replica.
- Added a readiness health check that reads and reaffirms the current mark.
- Added opt-in PostgreSQL tests for migration idempotence, monotonicity, concurrent writers, invalid values, outage readiness, and a simulated Redis rollback/recovery against the durable PostgreSQL mark.

## Startup and registration

The host must run `PostgresSecurityWarrantRevocationEpochAnchorMigrator.Apply(dataSource)` during
controlled startup, before accepting warrant-authorized traffic. Register the anchor with
`AddFoundginePostgresWarrantRevocationEpochAnchor(dataSource)` and pass the resulting
`ISecurityWarrantRevocationEpochAnchor` to `AnchoredSecurityWarrantRevocationStore`.

The migration is intentionally explicit: an authorization request must not silently create or
repair security state while the service is starting to serve traffic.

## Consistency and durability boundaries

- The atomic upsert serializes concurrent writers and cannot lower the stored mark.
- Reads are strongly consistent only when the supplied data source routes to the current writable
  PostgreSQL primary. Do not configure this data source to use a lagging read replica or a load
  balancer that may route reads to replicas.
- `synchronous_commit=on` requests local WAL durability before the advance returns. It does not,
  by itself, guarantee that a promoted standby contains the latest commit.
- To claim durable survival across PostgreSQL primary failover, deployment must configure and
  operate synchronous replication with an appropriate synchronous-standby policy, fencing,
  and promotion procedure. Asynchronous failover can lose a previously acknowledged anchor update.
- PostgreSQL must be operationally independent of the Redis primary and its failure domain.
- Anchor outage, missing migration, permissions failure, or connection failure must fail closed.

## Verification status

The source and project integration were reviewed. The environment used to prepare this checkpoint
has no `dotnet` executable and no configured PostgreSQL endpoint, so compilation, xUnit execution,
concurrent live-database testing, actual Redis/PostgreSQL combined integration, PostgreSQL failover testing, and ASP.NET Core host integration have not been
executed here. The rollback/recovery regression uses a controlled sequence-store test double with the real PostgreSQL anchor. The PostgreSQL tests are opt-in via `FOUNDGINE_POSTGRES_CONNECTION_STRING`.

This checkpoint implements the adapter and readiness component; it does not constitute production
HA certification. Validate synchronous replication, backup/restore, fencing, and Redis rollback
combined with PostgreSQL recovery in the target deployment before declaring distributed warrant
revocation production-ready.
