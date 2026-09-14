using Foundgine.Core.Abstractions;
using Foundgine.Core.Execution.Mutation;
using Foundgine.Extensions.GraphQL.HotChocolate;
using Foundgine.Core.Semantic.Metadata;
using Foundgine.Core.Semantic.Planning.Mutation;
using Foundgine.Core.Semantic;
using Xunit;

namespace Foundgine.Extensions.GraphQL.HotChocolate.Tests;

/// <summary>
/// GraphQL mutation acceptance proof. The adapter must consume the
/// complete protocol surface exercised by normal clients and converge on the
/// existing provider-neutral mutation/result contracts.
/// </summary>
public sealed class GraphQLMutationAcceptanceTests
{
    [Fact]
    public void Complete_mutation_document_converges_to_existing_intent_and_result_pipeline()
    {
        var (model, registry) = BuildCustomerAccount();
        var adapter = new HotChocolateMutationAdapter(model, registry);

        var document = """
                       query CustomerQuery {
                         customer { id }
                       }

                       mutation CreateCustomer($input: CustomerInput!) {
                         createCustomer(input: $input) {
                           ...CustomerPayload
                         }
                       }

                       mutation UpdateCustomer($input: CustomerInput!, $where: CustomerWhereInput!) {
                         updateCustomer(input: $input, where: $where) { id }
                       }

                       fragment CustomerPayload on Customer {
                         customerId: id
                         displayName: name
                         accounts {
                           ...AccountPayload
                         }
                       }

                       fragment AccountPayload on Account {
                         accountId: id
                         accountName: name
                       }
                       """;

        var adapted = adapter.AdaptWithResultShape(
            document,
            new Dictionary<string, object?>
            {
                ["input"] = new Dictionary<string, object?>
                {
                    ["name"] = "Ada",
                    ["accounts"] = new[]
                    {
                        new Dictionary<string, object?>
                        {
                            ["name"] = "Checking"
                        }
                    }
                }
            },
            "CreateCustomer");

        var root = Assert.IsType<MutationIntent>(adapted.Intent.Mutation);

        Assert.Equal(MutationKind.Create, root.Kind);
        Assert.Equal(new EntityId(1), root.Entity);
        Assert.Equal("Ada", Assert.Single(root.Fields).Value);
        Assert.Equal(
            [new FieldId(1), new FieldId(2)],
            root.ReturnFields);

        var child = Assert.Single(adapted.Intent.Children);

        Assert.Equal(
            new RelationshipId(10),
            child.Relationship);

        var childMutation =
            Assert.IsType<MutationIntent>(child.Mutation.Mutation);

        Assert.Equal(
            MutationKind.Create,
            childMutation.Kind);

        Assert.Equal(
            [new FieldId(1), new FieldId(3)],
            childMutation.ReturnFields);

        Assert.Equal(
            [
                new GraphQLMutationResultField(
                    new FieldId(1),
                    "customerId"),

                new GraphQLMutationResultField(
                    new FieldId(2),
                    "displayName")
            ],
            adapted.ResultShape.Fields);

        var relationship =
            Assert.Single(adapted.ResultShape.Relationships);

        Assert.Equal(
            "accounts",
            relationship.ResponseName);

        Assert.Equal(
            [
                new GraphQLMutationResultField(
                    new FieldId(1),
                    "accountId"),

                new GraphQLMutationResultField(
                    new FieldId(3),
                    "accountName")
            ],
            relationship.Shape.Fields);

        var materialized =
            new MutationResultMaterializer(model).Materialize(
                adapted.Intent,
                new MutationBatchResult(
                [
                    new MutationResult(
                        1,
                        new Dictionary<FieldId, object?>
                        {
                            [new FieldId(1)] = 42L,
                            [new FieldId(2)] = "Ada"
                        }),

                    new MutationResult(
                        1,
                        new Dictionary<FieldId, object?>
                        {
                            [new FieldId(1)] = 7L,
                            [new FieldId(3)] = "Checking"
                        })
                ]));

        var shaped =
            Assert.IsType<Dictionary<string, object?>>(
                GraphQLMutationResultShaper.ShapeRoot(
                    materialized,
                    adapted.ResultShape));

        Assert.Equal(
            42L,
            shaped["customerId"]);

        Assert.Equal(
            "Ada",
            shaped["displayName"]);

        var accounts =
            Assert.IsAssignableFrom<System.Collections.IEnumerable>(
                shaped["accounts"]);

        var account =
            Assert.IsType<Dictionary<string, object?>>(
                ((System.Collections.IEnumerable)accounts)
                .Cast<object>()
                .Single());

        Assert.Equal(
            7L,
            account["accountId"]);

        Assert.Equal(
            "Checking",
            account["accountName"]);
    }

    [Fact]
    public void Complete_boundary_rejects_unsupported_graphql_directives_without_leaking_them_downstream()
    {
        var (model, registry) = BuildCustomerAccount();
        var adapter = new HotChocolateMutationAdapter(model, registry);

        var ex = Assert.Throws<InvalidOperationException>(() => adapter.Adapt("""
                                                                              mutation CreateCustomer {
                                                                                createCustomer(input: { name: "Ada" }) @skip(if: false) {
                                                                                  id
                                                                                }
                                                                              }
                                                                              """));

        Assert.Contains(
            "directives",
            ex.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Complete_boundary_preserves_zero_row_semantics()
    {
        var (model, registry) = BuildCustomerAccount();
        var adapter = new HotChocolateMutationAdapter(model, registry);

        var adapted = adapter.AdaptWithResultShape(
            """
            mutation UpdateCustomer($input: CustomerInput!, $where: CustomerWhereInput!) {
              updateCustomer(input: $input, where: $where) {
                customerId: id
                displayName: name
              }
            }
            """,
            new Dictionary<string, object?>
            {
                ["input"] = new Dictionary<string, object?>
                {
                    ["name"] = "Ada"
                },

                ["where"] = new Dictionary<string, object?>
                {
                    ["id"] = new Dictionary<string, object?>
                    {
                        ["eq"] = 999L
                    }
                }
            });

        var materialized =
            new MutationResultMaterializer(model).Materialize(
                adapted.Intent,
                new MutationBatchResult(
                [
                    new MutationResult(
                        0,
                        new Dictionary<FieldId, object?>())
                ]));

        Assert.Null(
            GraphQLMutationResultShaper.ShapeRoot(
                materialized,
                adapted.ResultShape));
    }

    private static
        (SemanticModel Model, MetadataRegistry Registry)
        BuildCustomerAccount()
    {
        var customer = new EntityId(1);
        var account = new EntityId(2);
        var relationship = new RelationshipId(10);

        var registry = new MetadataRegistry();

        registry.Register(
            new EntityMetadata(
                customer,
                "Customer",
                [
                    new ColumnMetadata(
                        new ColumnId(1),
                        "Id"),

                    new ColumnMetadata(
                        new ColumnId(2),
                        "Name")
                ],
                Fields:
                [
                    new FieldMetadata(
                        new FieldId(1),
                        "Id",
                        typeof(long),
                        new ColumnReference(
                            customer,
                            new ColumnId(1))),

                    new FieldMetadata(
                        new FieldId(2),
                        "Name",
                        typeof(string),
                        new ColumnReference(
                            customer,
                            new ColumnId(2)))
                ],
                PrimaryKey:
                new ColumnReference(
                    customer,
                    new ColumnId(1))));

        registry.Register(
            new EntityMetadata(
                account,
                "Account",
                [
                    new ColumnMetadata(
                        new ColumnId(1),
                        "Id"),

                    new ColumnMetadata(
                        new ColumnId(2),
                        "CustomerId"),

                    new ColumnMetadata(
                        new ColumnId(3),
                        "Name")
                ],
                Fields:
                [
                    new FieldMetadata(
                        new FieldId(1),
                        "Id",
                        typeof(long),
                        new ColumnReference(
                            account,
                            new ColumnId(1))),

                    new FieldMetadata(
                        new FieldId(2),
                        "CustomerId",
                        typeof(long),
                        new ColumnReference(
                            account,
                            new ColumnId(2))),

                    new FieldMetadata(
                        new FieldId(3),
                        "Name",
                        typeof(string),
                        new ColumnReference(
                            account,
                            new ColumnId(3)))
                ],
                PrimaryKey:
                new ColumnReference(
                    account,
                    new ColumnId(1))));

        registry.Register(
            new RelationshipMetadata(
                relationship,
                customer,
                account,
                "Accounts",
                new ColumnReference(
                    customer,
                    new ColumnId(1)),
                new ColumnReference(
                    account,
                    new ColumnId(2))));

        var model =
            new SemanticModelBuilder()
                .Entity(
                    customer,
                    "Customer",
                    e => e
                        .Identity(
                            new FieldId(1),
                            "Id")
                        .Field(
                            new FieldId(2),
                            "Name",
                            typeof(string))
                        .Relationship(
                            relationship,
                            "Accounts",
                            account,
                            RelationshipCardinality.Many))
                .Entity(
                    account,
                    "Account",
                    e => e
                        .Identity(
                            new FieldId(1),
                            "Id")
                        .Field(
                            new FieldId(2),
                            "CustomerId",
                            typeof(long))
                        .Field(
                            new FieldId(3),
                            "Name",
                            typeof(string)))
                .Build();

        return (model, registry);
    }
}