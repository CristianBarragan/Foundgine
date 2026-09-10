# Foundgine High-Assurance Postgres — Benchmark Fixture

Real PostgreSQL execution for the `TransferFunds` capability.

This benchmark fixture intentionally keeps the business semantic contract in `Foundgine.HighAssurance.Banking` and adds only the provider boundary: transaction, row locking, idempotency serialization, database mutation, audit persistence, and execution evidence.

Set `FOUNDGINE_POSTGRES_CONNECTION` when running the integration tests.

## Authorization evidence atomicity

The high-assurance transfer executor can bind authorization evidence to a PostgreSQL transaction with `PostgresAuthorizationContextStore`.

The store reads the authoritative `(actor_id, tenant_id)` authorization context with `SELECT ... FOR UPDATE` and holds that row lock until the mutation transaction commits or rolls back. The returned `AuthorizationDecision` must match the stored version and fingerprint both before mutation and at the final commit gate.

This is stronger than an application-only version/fingerprint comparison: a concurrent authorization update cannot commit between the final evidence read and the mutation commit. If the authoritative context is configured but missing, execution fails closed.

The authorization-context version and fingerprint remain execution-time evidence and are not part of semantic/provider-plan cache identity.
