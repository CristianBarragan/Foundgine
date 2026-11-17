# Warrant revocation checkpoint 5 — shared Redis authority

## Scope

This increment adds a shared Redis-protocol implementation of `ISecurityWarrantRevocationStore` and closes a check-then-snapshot race in the common revocation guard. It builds on checkpoint 4 and does not change warrant signing, delegation-chain construction, replay semantics, or mutation transaction semantics.

## Changes

- Added `RedisSecurityWarrantRevocationStore` using the existing `IRedisScriptBackend` abstraction.
- Revocation writes use one Lua script to atomically create the immutable warrant-identity record, index the warrant digest, and advance the shared sequence.
- Repeated revocation requests advance the observation sequence while retaining the first revocation record, matching `MemorySecurityWarrantRevocationStore` behavior.
- Leaf identity checks and digest/ancestor checks are available across independent Foundgine instances.
- All script keys carry the same `{warrant-revocations}` Redis Cluster hash tag.
- Backend errors fail closed; no in-memory fallback is attempted.
- The core revocation guard now brackets leaf and ancestor lookups with the monotonic sequence. If the sequence changes during those lookups, validation fails rather than capturing a newer sequence after a stale check.
- The in-memory store now serializes sequence changes and record visibility under one lock, avoiding an increment-before-insert observation window.
- Added opt-in live Redis tests for cross-instance visibility, repeated revocation, monotonic sequence allocation, and concurrent distinct revocations; added backend-failure tests.

## Consistency and operational boundary

The Redis implementation executes each Lua mutation atomically on the Redis primary and performs synchronous script reads through the configured backend. It does not provide consensus-level durability. Redis replication is commonly asynchronous; a primary failure can lose an acknowledged write depending on persistence, replication acknowledgement, and failover configuration. Production operators must configure those properties to match their threat model.

The sequence is global to the configured key prefix. Any revocation request advances it, so an execution snapshot can be invalidated by an unrelated revocation. This is intentionally conservative and may cause additional retries/denials under high revocation volume.

Revocation records currently have no TTL. Automatic pruning is intentionally omitted because safe retention requires a deployment-wide maximum warrant lifetime, clock-skew allowance, and a guarantee that no valid descendant can refer to an older ancestor. Removing a record too early could resurrect delegated authority.

## Tests added

- `RedisSecurityWarrantRevocationStoreTests.Backend_failure_fails_closed_for_reads_and_writes`
- `RedisSecurityWarrantRevocationStoreTests.Blank_digest_is_rejected_before_backend_access`
- `RedisSecurityWarrantRevocationStoreTests.Revocation_is_shared_idempotent_and_monotonic_across_store_instances`
- `RedisSecurityWarrantRevocationStoreTests.Concurrent_distinct_revocations_receive_unique_monotonic_sequences`
- `SecurityWarrantRevocationSecurityTests.Validation_rejects_revocation_that_races_the_individual_revocation_lookups`

The Redis integration tests are opt-in via `FOUNDGINE_REDIS`.

## Verification status

- Source and test files reviewed after editing.
- Input archive ZIP integrity passed before editing.
- .NET build and xUnit tests were not run because the .NET SDK is not installed in this environment.
- No Redis server is installed/configured in this environment, so live Lua/Redis integration tests were not run.
- This checkpoint is not production-verified. CI must run the C# build and full test suites, including Redis integration tests against the supported Redis/Valkey/Garnet versions.
