package com.foundgine.samples.supplychain.advanced.jpa;

import org.flywaydb.core.Flyway;

import java.util.Map;

/**
 * Connection settings and schema migrations. The schema is owned by {@code db/migration/V*.sql} (Flyway), the
 * Java counterpart of the C# sample's EF migrations; demo rows live in the repeatable {@code db/demo} location
 * and are applied only when asked for.
 */
public final class AdvancedDatabase {
    private AdvancedDatabase() {}

    public static String url() {
        return env("SUPPLYCHAIN_JDBC_URL", "jdbc:postgresql://localhost:4429/foundgine_supply_chain");
    }

    public static String user() {
        return env("SUPPLYCHAIN_JDBC_USER", "foundgine");
    }

    public static String password() {
        return env("SUPPLYCHAIN_JDBC_PASSWORD", "foundgine");
    }

    public static Map<String, Object> jpaProperties() {
        return Map.of(
                "jakarta.persistence.jdbc.url", url(),
                "jakarta.persistence.jdbc.user", user(),
                "jakarta.persistence.jdbc.password", password());
    }

    /** Applies pending migrations; with {@code seedDemoData}, also the demo rows. Returns migrations applied. */
    public static int migrate(boolean seedDemoData) {
        var locations = seedDemoData
                ? new String[] {"classpath:db/migration", "classpath:db/demo"}
                : new String[] {"classpath:db/migration"};
        return Flyway.configure()
                .dataSource(url(), user(), password())
                .locations(locations)
                .load()
                .migrate()
                .migrationsExecuted;
    }

    /** SUPPLYCHAIN_MIGRATE_ON_START (default true) and SUPPLYCHAIN_SEED_DEMO (default true for this sample). */
    public static int migrateFromEnvironment() {
        if (!Boolean.parseBoolean(env("SUPPLYCHAIN_MIGRATE_ON_START", "true"))) return 0;
        return migrate(Boolean.parseBoolean(env("SUPPLYCHAIN_SEED_DEMO", "true")));
    }

    private static String env(String key, String fallback) {
        var v = System.getenv(key);
        return v == null || v.isBlank() ? fallback : v;
    }
}
