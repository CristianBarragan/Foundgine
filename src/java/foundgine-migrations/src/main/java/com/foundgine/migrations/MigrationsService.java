package com.foundgine.migrations;

import com.foundgine.migrations.diff.Operation;
import com.foundgine.migrations.diff.SchemaDiffer;
import com.foundgine.migrations.schema.DatabaseSchema;
import com.foundgine.migrations.sql.PostgresSqlGenerator;
import java.io.IOException;
import java.io.UncheckedIOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.time.Clock;
import java.time.ZoneOffset;
import java.time.format.DateTimeFormatter;
import java.util.ArrayList;
import java.util.List;
import java.util.Set;
import java.util.regex.Pattern;
import java.util.stream.Stream;

/**
 * The EF-style operations: add a migration from the model, list, remove the last one, script, and check whether
 * the model has changes no migration covers yet.
 */
public final class MigrationsService {
    private static final Pattern NAME = Pattern.compile("[A-Za-z][A-Za-z0-9_]*");
    private static final DateTimeFormatter VERSION = DateTimeFormatter.ofPattern("yyyyMMddHHmmss").withZone(ZoneOffset.UTC);

    public record AddResult(MigrationFile migration, List<Operation> operations, List<String> warnings) {}

    private final MigrationsWorkspace workspace;
    private final Clock clock;

    public MigrationsService(MigrationsWorkspace workspace) {
        this(workspace, Clock.systemUTC());
    }

    public MigrationsService(MigrationsWorkspace workspace, Clock clock) {
        this.workspace = workspace;
        this.clock = clock;
    }

    /** The schema the last migration left the model in; empty before the first migration. */
    public DatabaseSchema snapshot() {
        Path file = workspace.snapshotFile();
        return Files.exists(file) ? DatabaseSchema.read(file) : DatabaseSchema.empty();
    }

    public List<Operation> pendingOperations(DatabaseSchema model) {
        return SchemaDiffer.diff(snapshot(), model);
    }

    public boolean hasPendingModelChanges(DatabaseSchema model) {
        return !pendingOperations(model).isEmpty();
    }

    /** Writes {@code V<version>__<name>.sql}, its rollback script and the new snapshot. */
    public AddResult add(String name, DatabaseSchema model) {
        if (name == null || !NAME.matcher(name).matches())
            throw new IllegalArgumentException("Migration name '" + name + "' must start with a letter and use only letters, digits and underscores, for example InitialCreate.");
        if (list().stream().anyMatch(m -> m.name().equalsIgnoreCase(name)))
            throw new IllegalArgumentException("A migration named '" + name + "' already exists.");

        List<Operation> operations = pendingOperations(model);
        if (operations.isEmpty()) throw new IllegalStateException("The model has no changes since the last migration.");

        String version = nextVersion();
        MigrationFile file = new MigrationFile(version, name, workspace.migrationsDir().resolve("V" + version + "__" + name + ".sql"));
        String header = "-- Foundgine migration " + name + " (version " + version + ")\n"
                + "-- Generated from the fluent model. Edit it freely until it has been applied;\n"
                + "-- after that Flyway rejects any change to this file.\n\n";
        write(file.path(), header + PostgresSqlGenerator.up(operations));
        write(workspace.rollbackFile(version, name),
                "-- Rollback of " + name + ". Flyway Community cannot run this automatically; apply it by hand if you must.\n\n"
                        + PostgresSqlGenerator.down(operations));
        model.write(workspace.historyFile(version));
        model.write(workspace.snapshotFile());

        List<String> warnings = operations.stream().map(Operation::warning).filter(w -> w != null).toList();
        return new AddResult(file, operations, warnings);
    }

    /** All versioned migration files, oldest first, including hand-written ones. */
    public List<MigrationFile> list() {
        Path dir = workspace.migrationsDir();
        if (!Files.isDirectory(dir)) return List.of();
        try (Stream<Path> files = Files.list(dir)) {
            return files.map(MigrationFile::parse).flatMap(java.util.Optional::stream)
                    .sorted(MigrationFile::compareVersion).toList();
        } catch (IOException e) {
            throw new UncheckedIOException(e);
        }
    }

    /** Deletes the newest migration and restores the snapshot to what it was before. Refuses if it was applied. */
    public MigrationFile remove(Set<String> appliedVersions, boolean force) {
        List<MigrationFile> all = list();
        if (all.isEmpty()) throw new IllegalStateException("There are no migrations to remove.");
        MigrationFile last = all.get(all.size() - 1);
        if (!force && appliedVersions.contains(last.version()))
            throw new IllegalStateException("Migration " + last.version() + "__" + last.name()
                    + " has already been applied to the database. Add a new migration instead, or pass --force to delete the file anyway.");

        try {
            Files.delete(last.path());
            Files.deleteIfExists(workspace.rollbackFile(last.version(), last.name()));
            Files.deleteIfExists(workspace.historyFile(last.version()));
        } catch (IOException e) {
            throw new UncheckedIOException(e);
        }

        DatabaseSchema previous = null;
        for (int i = all.size() - 2; i >= 0 && previous == null; i--) {
            Path history = workspace.historyFile(all.get(i).version());
            if (Files.exists(history)) previous = DatabaseSchema.read(history);
        }
        if (previous == null) {
            try {
                Files.deleteIfExists(workspace.snapshotFile());
            } catch (IOException e) {
                throw new UncheckedIOException(e);
            }
        } else previous.write(workspace.snapshotFile());
        return last;
    }

    /** The SQL of every migration after {@code fromVersion} (exclusive), or all of them when null. */
    public String script(String fromVersion) {
        StringBuilder sql = new StringBuilder();
        MigrationFile from = fromVersion == null ? null : new MigrationFile(fromVersion, "", null);
        for (MigrationFile file : list()) {
            if (from != null && file.compareVersion(from) <= 0) continue;
            sql.append("-- ==== V").append(file.version()).append("__").append(file.name()).append(" ====\n");
            try {
                sql.append(Files.readString(file.path(), StandardCharsets.UTF_8)).append("\n");
            } catch (IOException e) {
                throw new UncheckedIOException(e);
            }
        }
        return sql.toString();
    }

    private String nextVersion() {
        String version = VERSION.format(clock.instant());
        List<MigrationFile> all = list();
        if (all.isEmpty()) return version;
        MigrationFile last = all.get(all.size() - 1);
        if (last.compareVersion(new MigrationFile(version, "", null)) < 0) return version;
        // Same second, or a clock behind the last migration: stay strictly after it so order is stable.
        return new java.math.BigInteger(last.version().replaceAll("[._]", "")).add(java.math.BigInteger.ONE).toString();
    }

    private static void write(Path file, String content) {
        try {
            Files.createDirectories(file.toAbsolutePath().getParent());
            Files.writeString(file, content, StandardCharsets.UTF_8);
        } catch (IOException e) {
            throw new UncheckedIOException(e);
        }
    }
}
