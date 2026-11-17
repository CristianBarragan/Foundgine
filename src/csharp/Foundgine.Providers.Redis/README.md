# Foundgine.Providers.Redis

Shared warrant replay protection and monotonic warrant revocation for horizontally scaled Foundgine hosts. Implements
`ISecurityWarrantReplayStore` with one `SET key 1 NX PX ttl` per consumed warrant, and
`ISecurityWarrantRevocationStore` with atomic Lua-backed revocation records and a shared monotonic sequence.
Works with Redis, Valkey and Microsoft Garnet (Redis protocol, built on FASTER).

```csharp
var mux = await ConnectionMultiplexer.ConnectAsync("garnet:6379,syncTimeout=1000");
builder.Services.AddFoundgine(o =>
{
    o.WarrantReplayStore = new RedisSecurityWarrantReplayStore(new StackExchangeReplayBackend(mux));
    // ...
});
```

- **Fails closed.** An unreachable server or error reply throws `InvalidOperationException`; the request is denied.
  Set `syncTimeout` so a hung server cannot block requests, and run the server replicated if an outage must not stop traffic.
- **Bounded storage.** Entries expire at warrant expiry plus one minute of clock-skew grace.
- **No raw nonces stored.** Keys are `prefix + SHA-256(warrantId 0x1F nonce)`, identical to the file store and the Java
  `RedisSecurityWarrantReplayStore`. Use one `keyPrefix` per environment on a shared server.
- Persistence matters: a server that loses data on restart forgets consumed nonces. Enable AOF/persistence (or accept
  that the replay window reopens for warrants still inside their lifetime).

## Shared warrant revocation

Register a shared revocation store alongside the replay store:

```csharp
var mux = await ConnectionMultiplexer.ConnectAsync("redis:6379,syncTimeout=1000");
builder.Services.AddSingleton<ISecurityWarrantRevocationStore>(
    _ => new RedisSecurityWarrantRevocationStore(new StackExchangeScriptBackend(mux)));
```

- **Atomic mutation.** One Lua script writes the warrant identity record, digest index, and increments the global
  revocation sequence. Repeated revocation attempts advance the observation sequence while preserving the first
  immutable record, matching the core in-memory store semantics.
- **Cluster-compatible keys.** All script keys include the same `{warrant-revocations}` hash tag and therefore share
  one Redis Cluster slot. The configurable prefix should still be unique per environment/application.
- **Fail closed.** Sequence reads, revocation lookups, and writes throw on Redis errors; callers must deny the request.
- **Execution race detection.** Core warrant validation brackets all leaf/ancestor lookups with the monotonic sequence.
  A revocation that races those reads causes validation to fail; execution snapshots detect later sequence changes.
- **Durability boundary.** Redis primary operations are serialized, but asynchronous replication can lose acknowledged
  writes during failover depending on server configuration. Enable persistence and choose replication acknowledgement
  and failover settings appropriate to the threat model. This provider does not claim consensus-level durability or
  a bounded replication-lag guarantee.

The live integration tests run when `FOUNDGINE_REDIS` is set. They cover cross-instance visibility, idempotent records,
monotonic sequencing, and concurrent distinct revocations.

## Readiness and rollback detection

`RedisSecurityWarrantRevocationStore.Probe()` performs one bounded Lua round trip that reads the
sequence and verifies a temporary write/read/delete on the configured Redis primary. Use it for
readiness (not liveness): if it throws, do not route warrant-backed traffic to that host. The probe
key has a five-second expiry as a cleanup backstop.

Each store instance also remembers the highest revocation sequence it has observed. If a later
primary read returns a lower value, the store fails closed with a sequence-regression error. This
is a process-local rollback detector; it does **not** protect a newly started process that has no
prior high-water mark, and it cannot recover revocation records lost by Redis. After a detected
regression, keep the affected host out of service until the Redis state is restored or an operator
performs a controlled recovery.

For production deployments, configure and test all of the following against the actual Redis
version and topology:

- AOF or equivalent persistence, with an fsync policy selected for the acceptable loss window.
- Replicas in separate failure domains and an explicit failover policy.
- `min-replicas-to-write` and `min-replicas-max-lag` where supported; these reduce exposure but do
  not provide consensus or prove that a particular write reached a replica.
- Monitoring for replication offset/lag, persistence health, failover events, and sequence
  regressions.
- A recovery procedure that compares/restores the authoritative revocation state before traffic
  is re-enabled. Never reset the sequence to make a failed health check pass.

The provider deliberately does not claim consensus durability, linearizability across failover,
or a bounded revocation-loss window. Redis `WAIT`/replication acknowledgements are not currently
part of the provider contract. A successful `Probe()` establishes primary read/write availability
at that moment only; it is not evidence of replica durability.

## Durable cross-process rollback anchor (checkpoints 7–8)

For deployments that need a newly started host to detect Redis state rollback, wrap the Redis store with
`AnchoredSecurityWarrantRevocationStore` and provide an `ISecurityWarrantRevocationEpochAnchor` implementation:

```csharp
var dataSource = NpgsqlDataSource.Create(postgresConnectionString);
PostgresSecurityWarrantRevocationEpochAnchorMigrator.Apply(dataSource);
services.AddFoundginePostgresWarrantRevocationEpochAnchor(dataSource);

// Resolve ISecurityWarrantRevocationEpochAnchor from DI and compose it with the Redis store.
ISecurityWarrantRevocationEpochAnchor durableAnchor =
    serviceProvider.GetRequiredService<ISecurityWarrantRevocationEpochAnchor>();
ISecurityWarrantRevocationStore revocations = new AnchoredSecurityWarrantRevocationStore(
    new RedisSecurityWarrantRevocationStore(new StackExchangeScriptBackend(mux)), durableAnchor);
```

The anchor contract remains in Core and is consumed by the Redis provider; the concrete PostgreSQL implementation lives in `Foundgine.Providers`. It must be shared by every host and atomically advance
`max(current, sequence)`, and durably commit before returning. A successful `Revoke` is not returned to its caller
until that sequence is anchored. On reads, the wrapper compares Redis with the durable high-water mark before and
after the revocation lookup. A Redis sequence below the anchor, an anchor outage, or a concurrent anchor advance
causes a fail-closed exception. This closes the fresh-process gap in the process-local high-water detector, provided
the configured anchor really has the documented durability and consistency properties.

Do not implement the anchor using process memory, a local ephemeral file, or the same Redis primary whose rollback it
is intended to detect. A relational implementation should use a single-row monotonic update (for example, an atomic
`GREATEST`/conditional update inside a committed transaction) and a strongly consistent read. `Foundgine.Providers` now includes `PostgresSecurityWarrantRevocationEpochAnchor`, its explicit migrator, and a readiness
health check. The PostgreSQL data source must target the writable primary. Its transactional upsert uses
`GREATEST` and `synchronous_commit=on`; surviving primary failover still requires a correctly configured synchronous
replication/fencing policy. See [`warrant-revocation-checkpoint-8.md`](../../../docs/security/warrant-revocation-checkpoint-8.md)
for deployment constraints and verification limits.
