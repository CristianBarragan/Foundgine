# Warrant revocation checkpoint 6 — readiness and rollback detection

## Scope

This increment hardens the Redis-backed `ISecurityWarrantRevocationStore` operational boundary.
It does not claim to solve Redis consensus, replication durability, or failover consistency.

## Changes

- Added `RedisSecurityWarrantRevocationStore.Probe()`, a Lua-backed read/write/read/delete
  readiness probe using a unique key with a five-second expiry backstop.
- Added a process-local high-water mark for the observed global revocation sequence. A later
  lower sequence is treated as possible state rollback and throws, failing closed.
- Changed the revocation Lua response to return both the immutable record sequence and the current
  global sequence. This matters because repeated revocation requests intentionally advance the
  global observation sequence while preserving the first revocation record.
- Corrected the live Redis test expectation to assert that repeated revocation advances the global
  sequence while returning the original record.
- Added tests for sequence regression and readiness probe success/failure.
- Documented Redis persistence, replica lag, failover, monitoring, and recovery requirements.

## Security semantics and limitations

The high-water mark is local to one store instance. It detects rollback only after that instance has
observed a higher sequence. A fresh process cannot infer that Redis lost a previously acknowledged
write from the sequence alone. The provider therefore still requires operators to keep a host out of
service after a regression and restore authoritative state before re-admission.

`Probe()` proves that the configured primary can execute the probe script and perform a temporary
write/read/delete at the time of the check. It does not prove replication acknowledgement, AOF
fsync, replica promotion safety, or durability across failover.

Redis `min-replicas-to-write` and `min-replicas-max-lag` can reduce risk but are not consensus. The
provider does not implement `WAIT`/`WAITAOF`, a durable external epoch, or consensus-backed storage.
No claim of zero-loss revocation durability or linearizability across failover is made.

## Tests added/updated

- `Sequence_regression_after_primary_rollback_fails_closed`
- `Readiness_probe_requires_a_successful_write_read_round_trip`
- `Readiness_probe_fails_closed_when_write_verification_fails`
- Updated live Redis assertion for monotonic sequence advancement on repeated revoke.

## Verification

- Source/archive integrity: verified when packaging.
- .NET compilation and xUnit execution: not run; the .NET SDK is unavailable in this environment.
- Live Redis integration: not run; no Redis service is available in this environment.
- Production/failover verification: outstanding and must be run against the deployment topology.
