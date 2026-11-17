# Java baseline arm

Spring GraphQL + plain JPA/Hibernate; hand-authored Flyway migration.

## Build

From `src/java` with JDK 27 and Maven 3.9.15:

```bash
mvn -pl benchmarks/supply-chain/spring-baseline -am -DskipTests package
```

Compose builds this project from the `src/java` build context. The runtime listens on its configured `SERVER_PORT` and serves GraphQL at `/graphql`. Both arms validate Keycloak JWT issuer and `supplychain-api` audience. This baseline also connects to the same Redis service and Java key namespace (`benchmark:java:`), but uses direct Lettuce/JDBC code rather than Foundgine provider classes. Its per-request filter performs the same identity/digest Redis lookups and sequence bracketing against the PostgreSQL durable high-water table. `/health/ready` probes Redis and PostgreSQL.

The benchmark harness owns deterministic seed loading. Do not enable Hibernate schema creation/update or seed demo data during benchmark runs.

## Verification status

Source wiring is present, including the direct Redis/JDBC baseline implementation and Flyway-owned anchor table. Compilation, container startup, Flyway history, Redis rollback detection, PostgreSQL primary behavior, schema parity, authorization vectors, and scenario response equivalence remain runtime acceptance gates. The project requires JDK 27 and Maven 3.9.15.
