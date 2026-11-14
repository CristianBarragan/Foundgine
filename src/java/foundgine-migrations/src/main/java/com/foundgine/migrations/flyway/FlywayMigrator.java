package com.foundgine.migrations.flyway;

import java.util.ArrayList;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Set;
import org.flywaydb.core.Flyway;
import org.flywaydb.core.api.MigrationInfo;

/**
 * The only class that talks to Flyway: applies pending migrations and reports what has been applied. Everything
 * else in this module produces or reads plain {@code V*.sql} files, so Flyway can also be run by its own CLI or
 * Maven plugin against the same directory.
 */
public final class FlywayMigrator {
    /** Where to run. {@code locations} use Flyway syntax, for example {@code filesystem:src/main/resources/db/migration}. */
    public record Settings(String url, String user, String password, List<String> locations) {
        public Settings {
            if (url == null || url.isBlank()) throw new IllegalArgumentException("A JDBC url is required.");
            locations = List.copyOf(locations);
        }
    }

    public record Status(String version, String description, String state, boolean applied) {}

    private FlywayMigrator() {}

    /** Applies pending migrations, up to {@code targetVersion} when given. Returns how many ran. */
    public static int migrate(Settings settings, String targetVersion) {
        var configuration = base(settings);
        if (targetVersion != null && !targetVersion.isBlank()) configuration.target(targetVersion);
        return configuration.load().migrate().migrationsExecuted;
    }

    /** Fails if an applied migration was edited or removed since it ran. */
    public static void validate(Settings settings) {
        base(settings).load().validate();
    }

    public static List<Status> status(Settings settings) {
        List<Status> result = new ArrayList<>();
        for (MigrationInfo info : base(settings).load().info().all()) {
            String version = info.getVersion() == null ? "(repeatable)" : info.getVersion().getVersion();
            result.add(new Status(version, info.getDescription(), info.getState().name(), info.getState().isApplied()));
        }
        return result;
    }

    /** Versions of versioned migrations that have been applied. */
    public static Set<String> appliedVersions(Settings settings) {
        Set<String> versions = new LinkedHashSet<>();
        for (Status status : status(settings)) if (status.applied() && !status.version().startsWith("(")) versions.add(status.version());
        return versions;
    }

    private static org.flywaydb.core.api.configuration.FluentConfiguration base(Settings settings) {
        return Flyway.configure()
                .dataSource(settings.url(), settings.user(), settings.password())
                .locations(settings.locations().toArray(String[]::new))
                .cleanDisabled(true);
    }
}
