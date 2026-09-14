using Foundgine.Core.Execution;
using Foundgine.Core.Semantic.Metadata;
using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Planning.Mutation;
using Foundgine.Providers.Storage.Sql.Mutation;
using Microsoft.Data.Sqlite;
using Xunit;
using ExecutionContext = Foundgine.Core.Execution.ExecutionContext;

namespace Foundgine.E2E.Tests;

public sealed class UpsertTests
{
    private static readonly EntityId Customer = new(501);

    [Fact]
    public void Insert_without_primary_key_returns_generated_identity()
    {
        var metadata = BuildMetadata();
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        using (var setup = connection.CreateCommand())
        {
            setup.CommandText = """
                                CREATE TABLE "Customer" (
                                    "Id" INTEGER PRIMARY KEY AUTOINCREMENT,
                                    "Name" TEXT NOT NULL
                                );
                                """;
            setup.ExecuteNonQuery();
        }

        var intent = new UpsertIntent(
            Customer,
            [new MutationFieldValue(new ColumnId(2), "Alice")],
            ReturnFields: [new FieldId(1), new FieldId(2)]);

        var plan = new MutationPlanner(metadata).Plan(intent);
        var sql = new SqlMutationCompiler(metadata).Compile(plan);
        var result = new SqlMutationExecutionProvider(connection)
            .Execute(sql, new ExecutionContext());

        Assert.Equal(1, result.AffectedRows);
        Assert.Equal(1L, result.ReturnedValues![new FieldId(1)]);
        Assert.Equal("Alice", result.ReturnedValues[new FieldId(2)]);
    }

    [Fact]
    public void Existing_primary_key_is_updated_and_returned()
    {
        var metadata = BuildMetadata();
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        using (var setup = connection.CreateCommand())
        {
            setup.CommandText = """
                                CREATE TABLE "Customer" (
                                    "Id" INTEGER PRIMARY KEY AUTOINCREMENT,
                                    "Name" TEXT NOT NULL
                                );
                                INSERT INTO "Customer" ("Name") VALUES ('Alice');
                                """;
            setup.ExecuteNonQuery();
        }

        var intent = new UpsertIntent(
            Customer,
            [
                new MutationFieldValue(new ColumnId(1), 1),
                new MutationFieldValue(new ColumnId(2), "Bob")
            ],
            ReturnFields: [new FieldId(1), new FieldId(2)]);

        var result = new SqlMutationExecutionProvider(connection)
            .Execute(
                new SqlMutationCompiler(metadata).Compile(new MutationPlanner(metadata).Plan(intent)),
                new ExecutionContext());

        Assert.Equal(1, result.AffectedRows);
        Assert.Equal(1L, result.ReturnedValues![new FieldId(1)]);
        Assert.Equal("Bob", result.ReturnedValues[new FieldId(2)]);
    }

    [Fact]
    public void Unchanged_upsert_uses_distinct_guard_and_still_returns_existing_row()
    {
        var metadata = BuildMetadata();
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        using (var setup = connection.CreateCommand())
        {
            setup.CommandText = """
                                CREATE TABLE "Customer" (
                                    "Id" INTEGER PRIMARY KEY AUTOINCREMENT,
                                    "Name" TEXT NOT NULL
                                );
                                INSERT INTO "Customer" ("Name") VALUES ('Alice');
                                """;
            setup.ExecuteNonQuery();
        }

        var intent = new UpsertIntent(
            Customer,
            [
                new MutationFieldValue(new ColumnId(1), 1),
                new MutationFieldValue(new ColumnId(2), "Alice")
            ],
            ReturnFields: [new FieldId(1), new FieldId(2)]);

        var plan = new SqlMutationCompiler(metadata).Compile(
            new MutationPlanner(metadata).Plan(intent));

        Assert.Contains(
            "IS DISTINCT FROM EXCLUDED.",
            plan.CommandText,
            StringComparison.Ordinal);
        Assert.NotNull(plan.FallbackCommandText);
        Assert.Contains(
            "WHERE \"Id\" IS NOT DISTINCT FROM @p0",
            plan.FallbackCommandText!,
            StringComparison.Ordinal);

        var result = new SqlMutationExecutionProvider(connection)
            .Execute(plan, new ExecutionContext());

        Assert.Equal(1, result.AffectedRows);
        Assert.Equal(1L, result.ReturnedValues![new FieldId(1)]);
        Assert.Equal("Alice", result.ReturnedValues[new FieldId(2)]);
    }

    [Fact]
    public void Custom_conflict_identity_is_used()
    {
        var metadata = BuildMetadata();
        var intent = new UpsertIntent(
            Customer,
            [new MutationFieldValue(new ColumnId(2), "Alice")],
            [new ColumnId(2)],
            [new FieldId(1), new FieldId(2)]);

        var sql = new SqlMutationCompiler(metadata).Compile(
            new MutationPlanner(metadata).Plan(intent));

        Assert.Contains("ON CONFLICT (\"Name\")", sql.CommandText, StringComparison.Ordinal);
    }

    private static MetadataRegistry BuildMetadata()
    {
        var registry = new MetadataRegistry();
        registry.Register(new EntityMetadata(
            Customer,
            "Customer",
            [
                new ColumnMetadata(new ColumnId(1), "Id"),
                new ColumnMetadata(new ColumnId(2), "Name")
            ],
            Fields:
            [
                new FieldMetadata(new FieldId(1), "Id", typeof(long),
                    new ColumnReference(Customer, new ColumnId(1))),
                new FieldMetadata(new FieldId(2), "Name", typeof(string),
                    new ColumnReference(Customer, new ColumnId(2)))
            ],
            PrimaryKey: new ColumnReference(Customer, new ColumnId(1))));
        return registry;
    }
}