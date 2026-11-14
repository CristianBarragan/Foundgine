package com.foundgine.migrations;

import java.nio.file.Path;

/**
 * Where migrations live. {@code migrationsDir} is the Flyway location (classpath {@code db/migration} once built);
 * {@code stateDir} holds what only the tooling needs: the model snapshot, one snapshot per migration, and the
 * generated rollback scripts. The state is kept outside the Flyway location because Flyway scans it recursively.
 */
public record MigrationsWorkspace(Path migrationsDir, Path stateDir) {
    public static MigrationsWorkspace forProject(Path projectDir) {
        return new MigrationsWorkspace(
                projectDir.resolve("src/main/resources/db/migration"),
                projectDir.resolve("src/main/foundgine/migrations"));
    }

    public Path snapshotFile() {
        return stateDir.resolve("model-snapshot.json");
    }

    public Path historyFile(String version) {
        return stateDir.resolve("history").resolve(version + ".json");
    }

    public Path rollbackFile(String version, String name) {
        return stateDir.resolve("rollback").resolve("V" + version + "__" + name + ".down.sql");
    }
}
