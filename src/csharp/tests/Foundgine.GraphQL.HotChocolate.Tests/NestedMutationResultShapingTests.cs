using Foundgine.Core.Abstractions;
using Foundgine.Core.Execution.Mutation;
using Foundgine.Extensions.GraphQL.HotChocolate;
using Foundgine.Core.Semantic.Metadata;
using Foundgine.Core.Semantic.Planning.Mutation;
using Foundgine.Core.Semantic;
using Xunit;

namespace Foundgine.Extensions.GraphQL.HotChocolate.Tests;

public sealed class NestedMutationResultShapingTests
{
    [Fact]
    public void Nested_relationship_result_is_shaped_with_aliases()
    {
        var (model, registry) = BuildCustomerAccount();
        var adapter = new HotChocolateMutationAdapter(model, registry);

        var adapted = adapter.AdaptWithResultShape("""
                                                   mutation {
                                                     createCustomer(input: {
                                                       name: "Ada"
                                                       accounts: [{ name: "Checking" }]
                                                     }) {
                                                       customerId: id
                                                       displayName: name
                                                       accounts {
                                                         accountId: id
                                                         accountName: name
                                                       }
                                                     }
                                                   }
                                                   """);

        var root = Assert.IsType<MutationIntent>(adapted.Intent.Mutation);
        Assert.Equal([new FieldId(1), new FieldId(2)], root.ReturnFields);
        var child = Assert.Single(adapted.Intent.Children);
        var childMutation = Assert.IsType<MutationIntent>(child.Mutation.Mutation);
        Assert.Equal([new FieldId(1), new FieldId(3)], childMutation.ReturnFields);

        var rootNode = new MutationMaterializedNode(0, new EntityId(1), new Dictionary<FieldId, object?>
        {
            [new FieldId(1)] = 42L,
            [new FieldId(2)] = "Ada"
        });
        var childNode = new MutationMaterializedNode(1, new EntityId(2), new Dictionary<FieldId, object?>
        {
            [new FieldId(1)] = 7L,
            [new FieldId(3)] = "Checking"
        });

        // The materializer owns child-tree construction; use its internal result path.
        var materialized = new MutationResultMaterializer(model).Materialize(
            adapted.Intent,
            new MutationBatchResult([
                new MutationResult(1, rootNode.Values),
                new MutationResult(1, childNode.Values)
            ]));

        var shaped = GraphQLMutationResultShaper.Shape(materialized.Roots.Single(), adapted.ResultShape);
        Assert.Equal(42L, shaped["customerId"]);
        Assert.Equal("Ada", shaped["displayName"]);
        var accounts = Assert.IsAssignableFrom<System.Collections.IEnumerable>(shaped["accounts"]);
        var account =
            Assert.IsType<Dictionary<string, object?>>(((System.Collections.IEnumerable)accounts).Cast<object>()
                .Single());
        Assert.Equal(7L, account["accountId"]);
        Assert.Equal("Checking", account["accountName"]);
    }

    [Fact]
    public void Nested_result_without_nested_mutation_is_rejected()
    {
        var (model, registry) = BuildCustomerAccount();
        var adapter = new HotChocolateMutationAdapter(model, registry);

        var ex = Assert.Throws<InvalidOperationException>(() => adapter.AdaptWithResultShape("""
            mutation {
              createCustomer(input: { name: "Ada" }) {
                id
                accounts { id name }
              }
            }
            """));

        Assert.Contains("no nested mutation", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Zero_affected_root_shapes_as_null()
    {
        var (model, registry) = BuildCustomerAccount();
        var adapter = new HotChocolateMutationAdapter(model, registry);
        var adapted = adapter.AdaptWithResultShape("""
                                                   mutation {
                                                     updateCustomer(input: { name: "Ada" }, where: { id: { eq: 99 } }) {
                                                       id
                                                       name
                                                     }
                                                   }
                                                   """);

        var materialized = new MutationResultMaterializer(model).Materialize(
            adapted.Intent,
            new MutationBatchResult([new MutationResult(0, new Dictionary<FieldId, object?>())]));

        Assert.Null(GraphQLMutationResultShaper.ShapeRoot(materialized, adapted.ResultShape));
    }

    [Fact]
    public void Missing_singular_nested_result_shapes_as_null()
    {
        var (model, registry) = BuildCustomerAccountWithPrimaryAccount();
        var adapter = new HotChocolateMutationAdapter(model, registry);
        var adapted = adapter.AdaptWithResultShape("""
                                                   mutation {
                                                     createCustomer(input: { name: "Ada" }) {
                                                       id
                                                       primary { id name }
                                                     }
                                                   }
                                                   """);

        var materialized = new MutationResultMaterializer(model).Materialize(
            adapted.Intent,
            new MutationBatchResult([
                new MutationResult(1, new Dictionary<FieldId, object?>
                    { [new FieldId(1)] = 42L })
            ]));

        var shaped = GraphQLMutationResultShaper.ShapeRoot(materialized, adapted.ResultShape);
        var result = Assert.IsType<Dictionary<string, object?>>(shaped);
        Assert.Null(result["primary"]);
    }

    [Fact]
    public void Zero_affected_singular_nested_result_shapes_as_null()
    {
        var (model, registry) = BuildCustomerAccountWithPrimaryAccount();
        var adapter = new HotChocolateMutationAdapter(model, registry);
        var adapted = adapter.AdaptWithResultShape("""
                                                   mutation {
                                                     createCustomer(input: { name: "Ada", primary: { name: "Checking" } }) {
                                                       id
                                                       primary { id name }
                                                     }
                                                   }
                                                   """);

        var materialized = new MutationResultMaterializer(model).Materialize(
            adapted.Intent,
            new MutationBatchResult([
                new MutationResult(1, new Dictionary<FieldId, object?> { [new FieldId(1)] = 42L }),
                new MutationResult(0, new Dictionary<FieldId, object?>())
            ]));

        var shaped = GraphQLMutationResultShaper.ShapeRoot(materialized, adapted.ResultShape);
        var result = Assert.IsType<Dictionary<string, object?>>(shaped);
        Assert.Null(result["primary"]);
    }

    private static (SemanticModel Model, MetadataRegistry Registry) BuildCustomerAccountWithPrimaryAccount()
    {
        var (model, registry) = BuildCustomerAccount();
        var customer = model.Get(new EntityId(1));
        var account = model.Get(new EntityId(2));
        var relationship = new RelationshipId(11);
        var registry2 = registry;
        // Rebuild the semantic model with a singular relationship while retaining the same metadata.
        var rebuilt = new SemanticModelBuilder()
            .Entity(new EntityId(1), "Customer", e => e
                .Identity(new FieldId(1), "Id")
                .Field(new FieldId(2), "Name", typeof(string))
                .Relationship(relationship, "Primary", new EntityId(2), RelationshipCardinality.One))
            .Entity(new EntityId(2), "Account", e => e
                .Identity(new FieldId(1), "Id")
                .Field(new FieldId(2), "CustomerId", typeof(long))
                .Field(new FieldId(3), "Name", typeof(string)))
            .Build();
        registry2.Register(new RelationshipMetadata(relationship, new EntityId(1), new EntityId(2), "Primary",
            new ColumnReference(new EntityId(1), new ColumnId(1)),
            new ColumnReference(new EntityId(2), new ColumnId(2))));
        return (rebuilt, registry2);
    }

    private static (SemanticModel Model, MetadataRegistry Registry) BuildCustomerAccount()
    {
        var customer = new EntityId(1);
        var account = new EntityId(2);
        var relationship = new RelationshipId(10);
        var registry = new MetadataRegistry();

        registry.Register(new EntityMetadata(customer, "Customer",
            [new ColumnMetadata(new ColumnId(1), "Id"), new ColumnMetadata(new ColumnId(2), "Name")],
            Fields:
            [
                new FieldMetadata(new FieldId(1), "Id", typeof(long), new ColumnReference(customer, new ColumnId(1))),
                new FieldMetadata(new FieldId(2), "Name", typeof(string),
                    new ColumnReference(customer, new ColumnId(2)))
            ], PrimaryKey: new ColumnReference(customer, new ColumnId(1))));

        registry.Register(new EntityMetadata(account, "Account",
            [
                new ColumnMetadata(new ColumnId(1), "Id"), new ColumnMetadata(new ColumnId(2), "CustomerId"),
                new ColumnMetadata(new ColumnId(3), "Name")
            ],
            Fields:
            [
                new FieldMetadata(new FieldId(1), "Id", typeof(long), new ColumnReference(account, new ColumnId(1))),
                new FieldMetadata(new FieldId(2), "CustomerId", typeof(long),
                    new ColumnReference(account, new ColumnId(2))),
                new FieldMetadata(new FieldId(3), "Name", typeof(string), new ColumnReference(account, new ColumnId(3)))
            ], PrimaryKey: new ColumnReference(account, new ColumnId(1))));

        registry.Register(new RelationshipMetadata(
            relationship, customer, account, "Accounts",
            new ColumnReference(customer, new ColumnId(1)),
            new ColumnReference(account, new ColumnId(2))));

        var model = new SemanticModelBuilder()
            .Entity(customer, "Customer", e => e
                .Identity(new FieldId(1), "Id")
                .Field(new FieldId(2), "Name", typeof(string))
                .Relationship(relationship, "Accounts", account, RelationshipCardinality.Many))
            .Entity(account, "Account", e => e
                .Identity(new FieldId(1), "Id")
                .Field(new FieldId(2), "CustomerId", typeof(long))
                .Field(new FieldId(3), "Name", typeof(string)))
            .Build();

        return (model, registry);
    }
}