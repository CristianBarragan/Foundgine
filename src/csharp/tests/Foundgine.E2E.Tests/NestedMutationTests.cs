using Foundgine.Core.Execution;
using Foundgine.Core.Semantic.Metadata;
using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Planning.Mutation;
using Foundgine.Providers.Storage.Sql.Mutation;
using Microsoft.Data.Sqlite;
using Xunit;
using ExecutionContext = Foundgine.Core.Execution.ExecutionContext;

namespace Foundgine.E2E.Tests;

public sealed class NestedMutationTests
{
    private static readonly EntityId Customer = new(701);
    private static readonly EntityId Account = new(702);
    private static readonly EntityId Transaction = new(703);

    [Fact]
    public void Nested_customer_account_transaction_mutation_is_flattened_and_executed_atomically()
    {
        var metadata = BuildMetadata();
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var setup = connection.CreateCommand();
        setup.CommandText = """
                            CREATE TABLE "Customer" ("Id" INTEGER PRIMARY KEY AUTOINCREMENT, "Name" TEXT NOT NULL);
                            CREATE TABLE "Account" ("Id" INTEGER PRIMARY KEY AUTOINCREMENT, "CustomerId" INTEGER NOT NULL, "Name" TEXT NOT NULL);
                            CREATE TABLE "Transaction" ("Id" INTEGER PRIMARY KEY AUTOINCREMENT, "AccountId" INTEGER NOT NULL, "Amount" INTEGER NOT NULL);
                            """;
        setup.ExecuteNonQuery();

        var nested = new NestedMutationIntent(
            new MutationIntent(
                Customer, MutationKind.Create,
                [new MutationFieldValue(new ColumnId(2), "Alice")],
                ReturnFields: [new FieldId(1), new FieldId(2)]),
            [
                new NestedMutationChild(
                    new RelationshipId(801),
                    new NestedMutationIntent(
                        new MutationIntent(
                            Account, MutationKind.Create,
                            [new MutationFieldValue(new ColumnId(3), "Primary")],
                            ReturnFields: [new FieldId(1), new FieldId(2), new FieldId(3)]),
                        [
                            new NestedMutationChild(
                                new RelationshipId(802),
                                new NestedMutationIntent(
                                    new MutationIntent(
                                        Transaction, MutationKind.Create,
                                        [new MutationFieldValue(new ColumnId(3), 250)],
                                        ReturnFields: [new FieldId(1), new FieldId(2), new FieldId(3)]),
                                    []))
                        ]))
            ]);

        var plan = new MutationPlanner(metadata).Plan(nested);
        Assert.Equal(3, plan.Operations.Count);
        Assert.Equal(2, plan.Dependencies.Count);
        Assert.Equal(new ColumnId(2), plan.Operations[1].Fields[^1].Column);
        Assert.Equal(new ColumnId(2), plan.Operations[2].Fields[^1].Column);

        var sql = new SqlMutationCompiler(metadata).Compile(plan);
        var result = new SqlMutationExecutionProvider(connection)
            .ExecuteBatch(sql, new ExecutionContext());

        Assert.Equal(3, result.Results.Count);
        Assert.Equal(1L, result.Results[0].ReturnedValues![new FieldId(1)]);
        Assert.Equal(1L, result.Results[1].ReturnedValues![new FieldId(1)]);
        Assert.Equal(1L, result.Results[2].ReturnedValues![new FieldId(1)]);

        using var verify = connection.CreateCommand();
        verify.CommandText =
            "SELECT a.CustomerId, t.AccountId, t.Amount FROM Account a JOIN \"Transaction\" t ON t.AccountId = a.Id;";
        using var reader = verify.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal(1L, reader.GetInt64(0));
        Assert.Equal(1L, reader.GetInt64(1));
        Assert.Equal(250L, reader.GetInt64(2));
    }

    [Fact]
    public void Nested_mutation_rejects_explicit_child_foreign_key()
    {
        var metadata = BuildMetadata();
        var nested = new NestedMutationIntent(
            new MutationIntent(Customer, MutationKind.Create,
                [new MutationFieldValue(new ColumnId(2), "Alice")],
                ReturnFields: [new FieldId(1)]),
            [
                new NestedMutationChild(
                    new RelationshipId(801),
                    new NestedMutationIntent(
                        new MutationIntent(Account, MutationKind.Create,
                            [
                                new MutationFieldValue(new ColumnId(2), 999),
                                new MutationFieldValue(new ColumnId(3), "Primary")
                            ],
                            ReturnFields: [new FieldId(1)]), []))
            ]);

        Assert.Throws<InvalidOperationException>(() => new MutationPlanner(metadata).Plan(nested));
    }

    [Fact]
    public void Nested_mutation_requires_valid_key_mapping()
    {
        var metadata = BuildMetadata();
        metadata.Register(new RelationshipMetadata(
            new RelationshipId(803), Customer, Account, "Broken",
            new ColumnReference(Customer, new ColumnId(999)),
            new ColumnReference(Account, new ColumnId(2))));
        var nested = new NestedMutationIntent(
            new MutationIntent(Customer, MutationKind.Create,
                [new MutationFieldValue(new ColumnId(2), "Alice")], ReturnFields: [new FieldId(1)]),
            [
                new NestedMutationChild(new RelationshipId(803),
                    new NestedMutationIntent(
                        new MutationIntent(Account, MutationKind.Create,
                            [new MutationFieldValue(new ColumnId(3), "Primary")], ReturnFields: [new FieldId(1)]), []))
            ]);

        Assert.Throws<InvalidOperationException>(() => new MutationPlanner(metadata).Plan(nested));
    }

    private static MetadataRegistry BuildMetadata()
    {
        var registry = new MetadataRegistry();
        registry.Register(new EntityMetadata(Customer, "Customer",
            [new ColumnMetadata(new ColumnId(1), "Id"), new ColumnMetadata(new ColumnId(2), "Name")],
            Fields:
            [
                new FieldMetadata(new FieldId(1), "Id", typeof(long), new ColumnReference(Customer, new ColumnId(1))),
                new FieldMetadata(new FieldId(2), "Name", typeof(string),
                    new ColumnReference(Customer, new ColumnId(2)))
            ],
            PrimaryKey: new ColumnReference(Customer, new ColumnId(1))));
        registry.Register(new EntityMetadata(Account, "Account",
            [
                new ColumnMetadata(new ColumnId(1), "Id"), new ColumnMetadata(new ColumnId(2), "CustomerId"),
                new ColumnMetadata(new ColumnId(3), "Name")
            ],
            Fields:
            [
                new FieldMetadata(new FieldId(1), "Id", typeof(long), new ColumnReference(Account, new ColumnId(1))),
                new FieldMetadata(new FieldId(2), "CustomerId", typeof(long),
                    new ColumnReference(Account, new ColumnId(2))),
                new FieldMetadata(new FieldId(3), "Name", typeof(string), new ColumnReference(Account, new ColumnId(3)))
            ],
            PrimaryKey: new ColumnReference(Account, new ColumnId(1))));
        registry.Register(new EntityMetadata(Transaction, "Transaction",
            [
                new ColumnMetadata(new ColumnId(1), "Id"), new ColumnMetadata(new ColumnId(2), "AccountId"),
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

        registry.Register(new RelationshipMetadata(
            new RelationshipId(801), Customer, Account, "Accounts",
            new ColumnReference(Customer, new ColumnId(1)),
            new ColumnReference(Account, new ColumnId(2))));
        registry.Register(new RelationshipMetadata(
            new RelationshipId(802), Account, Transaction, "Transactions",
            new ColumnReference(Account, new ColumnId(1)),
            new ColumnReference(Transaction, new ColumnId(2))));
        return registry;
    }
}