# Java Foundgine arm

Spring GraphQL + Foundgine JPA; generated Foundgine migration applied by Flyway.

## Build

From `src/java` with JDK 27 and Maven 3.9.15:

```bash
mvn -pl benchmarks/supply-chain/foundgine -am -DskipTests package
```

Compose builds this project from the `src/java` build context. The runtime listens on its configured `SERVER_PORT` and serves GraphQL at `/graphql`. Both arms validate Keycloak JWT issuer and `supplychain-api` audience. The Foundgine arm uses `RedisSecurityWarrantRevocationStore` + `AnchoredSecurityWarrantRevocationStore` with the PostgreSQL primary-backed epoch anchor. Its Redis namespace is `benchmark:java:`; the benchmark filter checks identity and digest revocation and brackets those lookups with sequence checks. `/health/ready` performs a Redis write/read/delete probe and checks the durable anchor.

The benchmark harness owns deterministic seed loading. Do not enable Hibernate schema creation/update or seed demo data during benchmark runs.

## Verification status

Source wiring is present, including the Redis provider and durable PostgreSQL anchor. Compilation, container startup, Flyway history, Redis rollback detection, PostgreSQL primary behavior, schema parity, authorization vectors, and scenario response equivalence remain runtime acceptance gates. The project requires JDK 27 and Maven 3.9.15.
