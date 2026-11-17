# Java warrant revocation — PostgreSQL durable epoch anchor

## Scope

This Java implementation brings the distributed revocation path into parity with the C# Checkpoint 8 architecture:

- `ISecurityWarrantRevocationEpochAnchor` is the provider-neutral durable high-water contract in `foundgine-core`.
- `RedisSecurityWarrantRevocationStore` stores revocation lookups and the Redis sequence using atomic Lua scripts. It works through a small `RevocationBackend` abstraction; `LettuceRevocationBackend` is the optional Lettuce adapter.
- `AnchoredSecurityWarrantRevocationStore` checks Redis against the independent durable high-water mark before and after lookups and only acknowledges a revoke after the durable mark has caught up.
- `PostgresSecurityWarrantRevocationEpochAnchor` uses a monotonic PostgreSQL upsert and a transaction with `synchronous_commit=on`.
- `PostgresSecurityWarrantRevocationEpochAnchorMigrator.apply(dataSource)` explicitly creates the singleton row and is safe to rerun under a PostgreSQL transaction advisory lock.
- `SecurityWarrantRevocationGuard` brackets revocation reads with the sequence to detect concurrent revocation while a warrant is being validated.

## Startup

Run the migration during controlled startup, before accepting warrant-authorized requests:

```java
PostgresSecurityWarrantRevocationEpochAnchorMigrator.apply(dataSource);
var anchor = new PostgresSecurityWarrantRevocationEpochAnchor(dataSource);
var redis = new RedisSecurityWarrantRevocationStore(new LettuceRevocationBackend(redisCommands));
var revocations = new AnchoredSecurityWarrantRevocationStore(redis, anchor);
```

Use a PostgreSQL `DataSource` routed to the writable primary for both reads and writes. Do not send anchor reads to a lagging replica. Configure Redis/Lettuce command timeouts so a stalled Redis endpoint fails closed rather than holding authorization requests indefinitely.

## Failure and durability contract

- Redis or PostgreSQL connection failure, missing migration, malformed Redis replies, negative values, or a Redis sequence below the durable mark must fail closed.
- The anchor is independent of Redis and must be operated in a separate failure domain.
- `synchronous_commit=on` protects local PostgreSQL WAL durability before a successful advance returns. It does not alone prove that an asynchronously replicated standby contains the acknowledged commit.
- Production failover claims require synchronous replication policy, fencing, promotion controls, and tested backup/restore procedures.
- The epoch anchor detects Redis sequence rollback. It does not reconstruct individual revocation records lost from Redis; the Redis data durability/replication and recovery strategy must preserve those records. If the revocation records themselves are lost, keep authorization unavailable until they are restored from a trusted source.

## Verification

The Java provider tests include deterministic unit coverage for strict Redis sequence parsing, Redis outage fail-closed behavior, anchor advancement, stale Redis detection, and anchor outage. They do not replace live PostgreSQL/Redis integration or primary failover testing.

Run from `src/java` with the project's configured JDK 27 and Maven version:

```bash
mvn -pl foundgine-providers -am test
mvn -pl foundgine-providers -am verify
```

Before declaring production readiness, add/run environment-backed tests against a writable PostgreSQL primary and Redis, including concurrent writers, Redis data rollback, PostgreSQL outage, and synchronous-standby promotion. The working environment used to prepare this package has Java 21 and no Maven executable, so Java compilation and Maven tests were not executed here.
