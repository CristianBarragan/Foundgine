package com.foundgine.migrations.schema;

import com.fasterxml.jackson.databind.ObjectMapper;
import com.fasterxml.jackson.databind.SerializationFeature;
import java.io.IOException;
import java.io.UncheckedIOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.Comparator;
import java.util.List;
import java.util.Optional;

/** Relational schema snapshot. */
public record DatabaseSchema(List<TableSchema> tables, List<PostgresExtensionSchema> extensions) {
    private static final ObjectMapper JSON = new ObjectMapper().enable(SerializationFeature.INDENT_OUTPUT);
    public DatabaseSchema(List<TableSchema> tables) { this(tables, List.of()); }
    public DatabaseSchema {
        tables = tables == null ? List.of() : tables.stream().sorted(Comparator.comparing(TableSchema::name)).toList();
        extensions = extensions == null ? List.of() : extensions.stream().sorted(Comparator.comparing(PostgresExtensionSchema::name)).toList();
    }
    public static DatabaseSchema empty() { return new DatabaseSchema(List.of(), List.of()); }
    public Optional<TableSchema> table(String name) { return tables.stream().filter(t -> t.name().equals(name)).findFirst(); }
    public Optional<PostgresExtensionSchema> extension(String name) { return extensions.stream().filter(e -> e.name().equals(name)).findFirst(); }
    public String toJson() { try { return JSON.writeValueAsString(this) + "\n"; } catch (IOException e) { throw new UncheckedIOException(e); } }
    public static DatabaseSchema fromJson(String json) { try { return JSON.readValue(json, DatabaseSchema.class); } catch (IOException e) { throw new UncheckedIOException("Model snapshot is not valid JSON: " + e.getMessage(), e); } }
    public static DatabaseSchema read(Path file) { try { return fromJson(Files.readString(file, StandardCharsets.UTF_8)); } catch (IOException e) { throw new UncheckedIOException(e); } }
    public void write(Path file) { try { Files.createDirectories(file.toAbsolutePath().getParent()); Files.writeString(file, toJson(), StandardCharsets.UTF_8); } catch (IOException e) { throw new UncheckedIOException(e); } }
}
