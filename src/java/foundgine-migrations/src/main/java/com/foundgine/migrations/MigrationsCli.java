package com.foundgine.migrations;

import com.foundgine.core.semantic.configuration.FoundgineModel;
import com.foundgine.migrations.diff.Operation;
import com.foundgine.migrations.flyway.FlywayMigrator;
import com.foundgine.migrations.schema.DatabaseSchema;
import com.foundgine.migrations.schema.SchemaBuilder;
import java.io.PrintStream;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.HashSet;
import java.util.List;
import java.util.Properties;
import java.util.Set;

/**
 * Command line counterpart of {@code dotnet ef}:
 *
 * <pre>
 * migrations add &lt;Name&gt;            write V&lt;version&gt;__Name.sql from the model changes
 * migrations remove [--force]      delete the newest migration and restore the snapshot
 * migrations list                  list migrations (and whether the database has them)
 * migrations script [fromVersion]  print the SQL of the migrations after fromVersion
 * migrations has-pending-model-changes   exit code 1 when the model differs from the snapshot
 * database update [targetVersion]  apply pending migrations through Flyway
 * database status                  show Flyway's view of every migration
 *
 * options: --project &lt;dir&gt;  --model &lt;FoundgineModel class&gt;  --url --user --password  --demo &lt;dir&gt;
 * </pre>
 *
 * Defaults come from {@code foundgine-migrations.properties} in the project directory ({@code model},
 * {@code jdbc.url}, {@code jdbc.user}, {@code jdbc.password}, {@code demo.dir}) and then from the environment
 * variables FOUNDGINE_JDBC_URL, FOUNDGINE_JDBC_USER and FOUNDGINE_JDBC_PASSWORD.
 */
public final class MigrationsCli {
    private final PrintStream out;
    private final PrintStream err;

    public MigrationsCli(PrintStream out, PrintStream err) {
        this.out = out;
        this.err = err;
    }

    public static void main(String[] args) {
        System.exit(new MigrationsCli(System.out, System.err).run(args));
    }

    public int run(String[] args) {
        try {
            return execute(args);
        } catch (IllegalArgumentException | IllegalStateException | UnsupportedOperationException e) {
            err.println("error: " + e.getMessage());
            return 2;
        } catch (RuntimeException e) {
            err.println("error: " + e);
            return 3;
        }
    }

    private int execute(String[] args) {
        List<String> positional = new ArrayList<>();
        java.util.Map<String, String> options = new java.util.HashMap<>();
        Set<String> flags = new HashSet<>();
        for (int i = 0; i < args.length; i++) {
            if (!args[i].startsWith("--")) positional.add(args[i]);
            else if (args[i].equals("--force")) flags.add("force");
            else if (i + 1 < args.length) options.put(args[i].substring(2), args[++i]);
            else throw new IllegalArgumentException("Option " + args[i] + " needs a value.");
        }
        if (positional.isEmpty()) {
            usage();
            return 1;
        }

        Path project = Path.of(options.getOrDefault("project", ".")).toAbsolutePath().normalize();
        Properties config = new Properties();
        Path configFile = project.resolve("foundgine-migrations.properties");
        if (Files.exists(configFile))
            try (var in = Files.newBufferedReader(configFile)) {
                config.load(in);
            } catch (java.io.IOException e) {
                throw new IllegalStateException("Cannot read " + configFile + ": " + e.getMessage());
            }

        MigrationsWorkspace workspace = MigrationsWorkspace.forProject(project);
        MigrationsService service = new MigrationsService(workspace);
        String group = positional.get(0);
        String command = positional.size() > 1 ? positional.get(1) : "";

        switch (group + " " + command) {
            case "migrations add" -> {
                if (positional.size() < 3) throw new IllegalArgumentException("Usage: migrations add <Name>");
                var result = service.add(positional.get(2), model(options, config));
                out.println("Created " + project.relativize(result.migration().path()));
                for (Operation operation : result.operations()) out.println("  " + describe(operation));
                for (String warning : result.warnings()) out.println("warning: " + warning);
                out.println("Review the file, then run: database update");
                return 0;
            }
            case "migrations remove" -> {
                Set<String> applied = flags.contains("force") ? Set.of() : appliedOrEmpty(options, config, project);
                var removed = service.remove(applied, flags.contains("force"));
                out.println("Removed V" + removed.version() + "__" + removed.name() + " and restored the model snapshot.");
                return 0;
            }
            case "migrations list" -> {
                Set<String> applied = appliedOrEmpty(options, config, project);
                var files = service.list();
                if (files.isEmpty()) out.println("No migrations.");
                for (var file : files)
                    out.println(file.version() + "  " + file.name() + (applied.contains(file.version()) ? "  (applied)" : ""));
                return 0;
            }
            case "migrations script" -> {
                out.print(service.script(positional.size() > 2 ? positional.get(2) : null));
                return 0;
            }
            case "migrations has-pending-model-changes" -> {
                var pending = service.pendingOperations(model(options, config));
                if (pending.isEmpty()) {
                    out.println("The model matches the last migration.");
                    return 0;
                }
                out.println("The model has changes that no migration covers:");
                for (Operation operation : pending) out.println("  " + describe(operation));
                out.println("Run: migrations add <Name>");
                return 1;
            }
            case "database update" -> {
                var settings = settings(options, config, project);
                int ran = FlywayMigrator.migrate(settings, positional.size() > 2 ? positional.get(2) : null);
                out.println(ran == 0 ? "Database is up to date." : "Applied " + ran + " migration(s).");
                return 0;
            }
            case "database status" -> {
                for (var status : FlywayMigrator.status(settings(options, config, project)))
                    out.println(status.version() + "  " + status.description() + "  " + status.state());
                return 0;
            }
            default -> {
                usage();
                return 1;
            }
        }
    }

    private DatabaseSchema model(java.util.Map<String, String> options, Properties config) {
        String className = options.getOrDefault("model", config.getProperty("model"));
        if (className == null)
            throw new IllegalArgumentException("Name the model class with --model <FoundgineModel subclass> or model=... in foundgine-migrations.properties.");
        try {
            Object instance = Class.forName(className).getDeclaredConstructor().newInstance();
            if (!(instance instanceof FoundgineModel model))
                throw new IllegalArgumentException(className + " is not a FoundgineModel.");
            return SchemaBuilder.fromMetadata(model.build());
        } catch (ReflectiveOperationException e) {
            throw new IllegalArgumentException("Cannot create " + className + " (it needs a public no-argument constructor on the classpath): " + e);
        }
    }

    private FlywayMigrator.Settings settings(java.util.Map<String, String> options, Properties config, Path project) {
        String url = pick(options.get("url"), config.getProperty("jdbc.url"), System.getenv("FOUNDGINE_JDBC_URL"));
        String user = pick(options.get("user"), config.getProperty("jdbc.user"), System.getenv("FOUNDGINE_JDBC_USER"));
        String password = pick(options.get("password"), config.getProperty("jdbc.password"), System.getenv("FOUNDGINE_JDBC_PASSWORD"));
        if (url == null) throw new IllegalArgumentException("No database: pass --url, set jdbc.url, or set FOUNDGINE_JDBC_URL.");
        List<String> locations = new ArrayList<>();
        locations.add("filesystem:" + MigrationsWorkspace.forProject(project).migrationsDir());
        String demo = options.getOrDefault("demo", config.getProperty("demo.dir"));
        if (demo != null) locations.add("filesystem:" + project.resolve(demo).normalize());
        return new FlywayMigrator.Settings(url, user, password, locations);
    }

    private Set<String> appliedOrEmpty(java.util.Map<String, String> options, Properties config, Path project) {
        try {
            return FlywayMigrator.appliedVersions(settings(options, config, project));
        } catch (RuntimeException e) {
            err.println("note: could not read applied migrations from the database (" + e.getMessage().lines().findFirst().orElse("") + ").");
            return Set.of();
        }
    }

    private static String pick(String... values) {
        for (String value : values) if (value != null && !value.isBlank()) return value;
        return null;
    }

    private static String describe(Operation operation) {
        return switch (operation) {
            case Operation.CreateTable o -> "create table " + o.table().name();
            case Operation.DropTable o -> "drop table " + o.table().name();
            case Operation.AddColumn o -> "add column " + o.table() + "." + o.column().name() + " " + o.column().type();
            case Operation.DropColumn o -> "drop column " + o.table() + "." + o.column().name();
            case Operation.AlterColumnType o -> "alter " + o.table() + "." + o.column() + " type " + o.from() + " -> " + o.to();
            case Operation.AlterColumnNullability o ->
                    "alter " + o.table() + "." + o.column() + (o.nullable() ? " drop not null" : " set not null");
            case Operation.CreateIndex o -> "create " + (o.index().unique() ? "unique " : "") + "index " + o.index().name();
            case Operation.DropIndex o -> "drop index " + o.index().name();
            case Operation.SetColumnDefault o -> "alter " + o.table() + "." + o.column()
                    + (o.expression() == null ? " drop default" : " set default " + o.expression());
            case Operation.AddForeignKey o -> "add foreign key " + o.foreignKey().name() + " on " + o.table();
            case Operation.DropForeignKey o -> "drop foreign key " + o.foreignKey().name() + " on " + o.table();
            case Operation.AddCheck o -> "add CHECK constraint " + o.check().name() + " on " + o.table();
            case Operation.DropCheck o -> "drop CHECK constraint " + o.check().name() + " on " + o.table();
            case Operation.CreateExtension o -> "create PostgreSQL extension " + o.extension().name();
            case Operation.DropExtension o -> "drop PostgreSQL extension " + o.extension().name();
        };
    }

    private void usage() {
        out.println("""
                Foundgine migrations
                  migrations add <Name>
                  migrations remove [--force]
                  migrations list
                  migrations script [fromVersion]
                  migrations has-pending-model-changes
                  database update [targetVersion]
                  database status
                options: --project <dir> --model <class> --url <jdbc> --user <u> --password <p> --demo <dir>""");
    }
}
