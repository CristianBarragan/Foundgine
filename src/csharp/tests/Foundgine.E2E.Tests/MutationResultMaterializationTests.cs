using Foundgine.Core.Execution.Mutation;
using Foundgine.Core.Semantic;
using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Planning.Mutation;
using Xunit;

namespace Foundgine.E2E.Tests;

public sealed class MutationResultMaterializationTests
{
    private static readonly EntityId Customer = new(701);
    private static readonly EntityId Account = new(702);
    private static readonly EntityId Transaction = new(703);

    [Fact]
    public void Nested_mutation_results_are_shaped_back_into_the_relationship_tree()
    {
        var model = BuildModel();
        var intent = new NestedMutationIntent(
            new MutationIntent(Customer, MutationKind.Create,
                [new MutationFieldValue(new ColumnId(2), "Alice")],
                ReturnFields: [new FieldId(1), new FieldId(2)]),
            [
                new NestedMutationChild(
                    new RelationshipId(801),
                    new NestedMutationIntent(
                        new MutationIntent(Account, MutationKind.Create,
                            [new MutationFieldValue(new ColumnId(3), "Primary")],
                            ReturnFields: [new FieldId(1), new FieldId(2), new FieldId(3)]),
                        [
                            new NestedMutationChild(
                                new RelationshipId(802),
                                new NestedMutationIntent(
                                    new MutationIntent(Transaction, MutationKind.Create,
                                        [new MutationFieldValue(new ColumnId(3), 250)],
                                        ReturnFields: [new FieldId(1), new FieldId(2), new FieldId(3)]),
                                    []))
                        ]))
            ]);

        var result = new MutationBatchResult([
            new MutationResult(1, new Dictionary<FieldId, object?>
            {
                [new FieldId(1)] = 11L, [new FieldId(2)] = "Alice"
            }),
            new MutationResult(1, new Dictionary<FieldId, object?>
            {
                [new FieldId(1)] = 21L, [new FieldId(2)] = 11L, [new FieldId(3)] = "Primary"
            }),
            new MutationResult(1, new Dictionary<FieldId, object?>
            {
                [new FieldId(1)] = 31L, [new FieldId(2)] = 21L, [new FieldId(3)] = 250L
            })
        ]);

        var materialized = new MutationResultMaterializer(model).Materialize(intent, result);

        var customer = Assert.Single(materialized.Roots);
        Assert.Equal(Customer, customer.EntityId);
        Assert.Equal("Alice", customer.Values[new FieldId(2)]);

        var account = Assert.Single(customer.Children[new RelationshipId(801)]);
        Assert.Equal(Account, account.EntityId);
        Assert.Equal(21L, account.Values[new FieldId(1)]);
        Assert.Equal(11L, account.Values[new FieldId(2)]);

        var transaction = Assert.Single(account.Children[new RelationshipId(802)]);
        Assert.Equal(Transaction, transaction.EntityId);
        Assert.Equal(31L, transaction.Values[new FieldId(1)]);
        Assert.Equal(21L, transaction.Values[new FieldId(2)]);
        Assert.Equal(250L, transaction.Values[new FieldId(3)]);
    }

    [Fact]
    public void Result_shape_rejects_an_operation_count_mismatch()
    {
        var model = BuildModel();
        var intent = new NestedMutationIntent(
            new MutationIntent(Customer, MutationKind.Create,
                [new MutationFieldValue(new ColumnId(2), "Alice")], ReturnFields: [new FieldId(1)]), []);

        Assert.Throws<InvalidOperationException>(() =>
            new MutationResultMaterializer(model).Materialize(
                intent,
                new MutationBatchResult([])));
    }

    private static SemanticModel BuildModel()
    {
        return new SemanticModelBuilder()
            .Entity(Customer, "Customer", e => e
                .Identity(new FieldId(1), "Id")
                .Field(new FieldId(1), "Id", typeof(long))
                .Field(new FieldId(2), "Name", typeof(string))
                .Relationship(new RelationshipId(801), "Accounts", Account, RelationshipCardinality.Many))
            .Entity(Account, "Account", e => e
                .Identity(new FieldId(1), "Id")
                .Field(new FieldId(1), "Id", typeof(long))
                .Field(new FieldId(2), "CustomerId", typeof(long))
                .Field(new FieldId(3), "Name", typeof(string))
                .Relationship(new RelationshipId(802), "Transactions", Transaction, RelationshipCardinality.Many))
            .Entity(Transaction, "Transaction", e => e
                .Identity(new FieldId(1), "Id")
                .Field(new FieldId(1), "Id", typeof(long))
                .Field(new FieldId(2), "AccountId", typeof(long))
                .Field(new FieldId(3), "Amount", typeof(long)))
            .Build();
    }
}