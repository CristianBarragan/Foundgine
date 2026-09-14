using Foundgine.Core.Execution;
using Foundgine.Core.Execution.Mutation;
using Foundgine.Core.Semantic.Metadata;
using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Planning.Mutation;
using Foundgine.Providers.Storage.Sql.Mutation;
using Foundgine.Providers.Storage.Sql.Mutation.Postgres;
using Microsoft.Data.Sqlite;
using Xunit;
using ExecutionContext = Foundgine.Core.Execution.ExecutionContext;

namespace Foundgine.E2E.Tests;

public sealed class MutationDependencyTests
{
    private static readonly EntityId Customer = new(601);
    private static readonly EntityId Account = new(602);
    private static readonly EntityId Transaction = new(603);

    [Fact]
    public void Generated_identity_flows_customer_to_account_to_transaction_in_one_transaction()
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
                                CREATE TABLE "Account" (
                                    "Id" INTEGER PRIMARY KEY AUTOINCREMENT,
                                    "CustomerId" INTEGER NOT NULL,
                                    "Name" TEXT NOT NULL
                                );
                                CREATE TABLE "Transaction" (
                                    "Id" INTEGER PRIMARY KEY AUTOINCREMENT,
                                    "AccountId" INTEGER NOT NULL,
                                    "Amount" INTEGER NOT NULL
                                );
                                """;
            setup.ExecuteNonQuery();
        }

        var batch = new MutationBatchIntent(
        [
            new MutationIntent(
                Customer,
                MutationKind.Create,
                [new MutationFieldValue(new ColumnId(2), "Alice")],
                ReturnFields: [new FieldId(1), new FieldId(2)]),

            new MutationIntent(
                Account,
                MutationKind.Create,
                [
                    MutationFieldValue.FromPrevious(
                        new ColumnId(2), 0, new FieldId(1)),
                    new MutationFieldValue(new ColumnId(3), "Primary")
                ],
                ReturnFields: [new FieldId(1), new FieldId(2)]),

            new MutationIntent(
                Transaction,
                MutationKind.Create,
                [
                    MutationFieldValue.FromPrevious(
                        new ColumnId(2), 1, new FieldId(1)),
                    new MutationFieldValue(new ColumnId(3), 250)
                ],
                ReturnFields: [new FieldId(1), new FieldId(2), new FieldId(3)])
        ]);

        var plan = new MutationPlanner(metadata).Plan(batch);
        Assert.Equal(2, plan.Dependencies.Count);

        var sql = new SqlMutationCompiler(metadata).Compile(plan);
        var result = new SqlMutationExecutionProvider(connection)
            .ExecuteBatch(sql, new ExecutionContext());

        Assert.Equal(3, result.Results.Count);
        Assert.Equal(1L, result.Results[0].ReturnedValues![new FieldId(1)]);
        Assert.Equal(1L, result.Results[1].ReturnedValues![new FieldId(1)]);
        Assert.Equal(1L, result.Results[2].ReturnedValues![new FieldId(1)]);

        using var verify = connection.CreateCommand();
        verify.CommandText = """
                             SELECT a.CustomerId, t.AccountId, t.Amount
                             FROM "Account" a
                             JOIN "Transaction" t ON t.AccountId = a.Id;
                             """;

        using var reader = verify.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal(1L, reader.GetInt64(0));
        Assert.Equal(1L, reader.GetInt64(1));
        Assert.Equal(250L, reader.GetInt64(2));
    }

    [Fact]
    public void Dependency_must_reference_an_earlier_returned_field()
    {
        var metadata = BuildMetadata();

        var batch = new MutationBatchIntent(
        [
            new MutationIntent(
                Account,
                MutationKind.Create,
                [MutationFieldValue.FromPrevious(new ColumnId(2), 1, new FieldId(1))],
                ReturnFields: [new FieldId(1)]),

            new MutationIntent(
                Customer,
                MutationKind.Create,
                [new MutationFieldValue(new ColumnId(2), "Alice")],
                ReturnFields: [new FieldId(1)])
        ]);

        Assert.Throws<InvalidOperationException>(() =>
            new MutationPlanner(metadata).Plan(batch));
    }

    [Fact]
    public void Failed_child_rolls_back_parent_and_dependency_chain()
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
                                CREATE TABLE "Account" (
                                    "Id" INTEGER PRIMARY KEY AUTOINCREMENT,
                                    "CustomerId" INTEGER NOT NULL,
                                    "Name" TEXT NOT NULL
                                );
                                """;
            setup.ExecuteNonQuery();
        }

        var batch = new MutationBatchIntent(
        [
            new MutationIntent(
                Customer,
                MutationKind.Create,
                [new MutationFieldValue(new ColumnId(2), "Alice")],
                ReturnFields: [new FieldId(1)]),

            new MutationIntent(
                Account,
                MutationKind.Create,
                [
                    MutationFieldValue.FromPrevious(new ColumnId(2), 0, new FieldId(1)),
                    new MutationFieldValue(new ColumnId(3), null)
                ],
                ReturnFields: [new FieldId(1)])
        ]);

        var plan = new MutationPlanner(metadata).Plan(batch);
        var sql = new SqlMutationCompiler(metadata).Compile(plan);

        Assert.Throws<SqliteException>(() =>
            new SqlMutationExecutionProvider(connection)
                .ExecuteBatch(sql, new ExecutionContext()));

        using var verify = connection.CreateCommand();
        verify.CommandText = """SELECT COUNT(*) FROM "Customer";""";
        Assert.Equal(0L, (long)verify.ExecuteScalar()!);
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

        registry.Register(new EntityMetadata(
            Account,
            "Account",
            [
                new ColumnMetadata(new ColumnId(1), "Id"),
                new ColumnMetadata(new ColumnId(2), "CustomerId"),
                new ColumnMetadata(new ColumnId(3), "Name")
            ],
            Fields:
            [
                new FieldMetadata(new FieldId(1), "Id", typeof(long),
                    new ColumnReference(Account, new ColumnId(1))),
                new FieldMetadata(new FieldId(2), "CustomerId", typeof(long),
                    new ColumnReference(Account, new ColumnId(2))),
                new FieldMetadata(new FieldId(3), "Name", typeof(string),
                    new ColumnReference(Account, new ColumnId(3)))
            ],
            PrimaryKey: new ColumnReference(Account, new ColumnId(1))));

        registry.Register(new EntityMetadata(
            Transaction,
            "Transaction",
            [
                new ColumnMetadata(new ColumnId(1), "Id"),
                new ColumnMetadata(new ColumnId(2), "AccountId"),
                new ColumnMetadata(new ColumnId(3), "Amount")
            ],
            Fields:
            [
                new FieldMetadata(new FieldId(1), "Id", typeof(long),
                    new ColumnReference(Transaction, new ColumnId(1))),
                new FieldMetadata(new FieldId(2), "AccountId", typeof(long),
                    new ColumnReference(Transaction, new ColumnId(2))),
                new FieldMetadata(new FieldId(3), "Amount", typeof(long),
                    new ColumnReference(Transaction, new ColumnId(3)))
            ],
            PrimaryKey: new ColumnReference(Transaction, new ColumnId(1))));

        return registry;
    }
}

public sealed class MutationDependencyGraphTests
{
    [Fact]
    public void Execution_dependency_graph_computes_levels_without_correlation_types()
    {
        var dependencies = new MutationDependency[]
        {
            new(0, 2, new FieldId(1), new ColumnId(3)),
            new(1, 2, new FieldId(1), new ColumnId(4))
        };

        var graph = new MutationDependencyGraph(dependencies);
        var levels = MutationDependencyLevels.Compute(3, graph.Dependencies);

        Assert.Equal(2, levels.Count);
        Assert.Equal([0, 1], levels[0]);
        Assert.Equal([2], levels[1]);
    }

    [Fact]
    public void Execution_dependency_graph_rejects_cycles()
    {
        var dependencies = new MutationDependency[]
        {
            new(0, 1, new FieldId(1), new ColumnId(2)),
            new(1, 0, new FieldId(1), new ColumnId(2))
        };

        Assert.Throws<InvalidOperationException>(() =>
            MutationDependencyLevels.Compute(2, dependencies));
    }

    [Fact]
    public void Execution_ir_is_the_only_input_to_provider_dependency_levels()
    {
        var operations = new[]
        {
            new MutationOperation(
                new MutationEntitySchema(
                    new EntityId(1),
                    "A",
                    new HashSet<ColumnId>(),
                    new Dictionary<FieldId, ColumnId?>(),
                    null),
                MutationKind.Create,
                [],
                null,
                null,
                [])
        };

        var ir = ExecutionMutationIR.From(
            new MutationBatchPlan(operations, []));

        var levels = MutationExecutionLevels.From(ir);
        Assert.Single(levels.Levels);
        Assert.Equal([0], levels.Levels[0]);

        var boundary = PostgresMutationBatchBoundary.From(ir);
        Assert.Single(boundary.DependencyLevels.Levels);
    }
}