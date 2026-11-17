# Warrant revocation checkpoint 7 — durable external epoch anchor

## Problem addressed

Checkpoint 6's rollback detector retained its high-water mark only in one process. A replacement process started
with a zero high-water mark and therefore could not detect that Redis had been restored from an older snapshot or
promoted from a lagging replica.

## Changes

- Added `ISecurityWarrantRevocationEpochAnchor` to the core warrant security contracts.
- Added `AnchoredSecurityWarrantRevocationStore`, a fail-closed decorator for a Redis-backed revocation store.
- The decorator compares Redis's sequence with a durable shared high-water mark, advances the anchor monotonically,
  and reads it back before accepting the observation.
- A successful revoke is not returned until the revocation sequence has been committed to the anchor.
- Revocation lookups are bracketed by anchor synchronization so an anchor advance racing a lookup fails closed.
- Added tests for fresh-host rollback detection, anchor outage, successful anchored revocation, and a concurrent
  anchor advance.
- Documented the external anchor contract and explicitly prohibited using the same Redis primary or process-local
  memory as the anchor.

## Required adapter guarantees

The repository now defines the contract and enforcement wrapper, but does not include a concrete durable database
adapter. An adapter must provide a strongly consistent read and an atomic, durable `max(current, sequence)` update
shared across every Foundgine instance. Its successful update must be committed before returning. Without such an
adapter, the checkpoint 6 limitation remains and fresh-process rollback detection is not available.

The anchor must be independent of the Redis primary being protected. Using a second Redis deployment may still have
its own failover semantics and must be assessed separately. A transactional relational database or consensus-backed
key/value store is a more appropriate authority when its consistency/durability guarantees meet the deployment's
requirements.

## Security semantics and limitations

- The wrapper protects acknowledged revocation writes: `Revoke` returns only after the durable anchor has advanced.
- If Redis commits a revoke but the anchor fails, the operation throws. The record may exist in Redis, but the caller
  must treat the operation as unsuccessful/unknown and retry or reconcile; it must not assume success.
- If Redis later rolls back below the anchor, the wrapper rejects the host, including after process restart.
- The wrapper does not create consensus between Redis and the anchor, and it cannot make a non-durable or eventually
  consistent anchor safe. The adapter's contract is security-critical.
- A failure between Redis mutation and anchor commit can leave an unacknowledged record in Redis. This is safe for
  authorization (extra revocation is conservative) but requires operational reconciliation.
- This is not a replacement for checkpoint 6 readiness probes, Redis persistence, replication monitoring, or
  deployment failover testing.

## Tests added

- `Fresh_host_detects_redis_rollback_using_durable_anchor`
- `Revoke_is_not_returned_until_sequence_is_anchored`
- `Anchor_failure_fails_closed_for_sequence_and_revocation`
- `Concurrently_advanced_anchor_rejects_a_stale_redis_observation`

## Verification

- ZIP integrity: verified after packaging.
- Source-level review: performed.
- .NET build/xUnit: not run; `dotnet` is not installed in this environment.
- Live Redis and concrete durable-anchor integration: not run.
- Production readiness: not established; a production adapter and its failure-mode tests remain required.
