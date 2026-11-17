# Java baseline migration provenance

`src/main/resources/db/migration/V1__baseline.sql` is the independent, hand-authored
Flyway migration for the Spring GraphQL + plain JPA/Hibernate benchmark arm.
It intentionally does not invoke `foundgine-migrations` and does not enable
Hibernate schema creation/update. Flyway must own DDL application.

The schema is aligned with the Java SupplyChain domain already present in
`src/java/samples/foundgine-supply-chain` and with `tools/foundgine-perf/seed/reset-java.sql`.
The Foundgine arm must continue to use the generated versioned migration under
its own `db/migration` location, produced by `migrations.sh` from its fluent model.

Verification required on a clean PostgreSQL database:

1. Start only the baseline app with Flyway enabled and Hibernate `ddl-auto=validate`.
2. Confirm `flyway_schema_history` contains exactly `V1__baseline.sql`.
3. Apply `seed/reset-java.sql`.
4. Run `fpbench schemaparity configs/supplychain-java.json` and confirm the two Java
   schemas match while their Flyway migration ids remain distinct.

This file's existence is not evidence that those runtime checks have passed.

The shared benchmark security infrastructure also creates `foundgine_warrant_revocation_epoch` in this arm database. The Foundgine arm uses the provider migrator; the baseline creates the same table from its Flyway migration and idempotent JDBC initializer. This table is included in schema-parity output and must remain structurally identical between the two Java arms.
