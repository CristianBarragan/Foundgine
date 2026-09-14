using Foundgine.Core.Abstractions;
using Foundgine.Core.Execution.Mutation;
using Foundgine.Extensions.GraphQL.HotChocolate;
using Foundgine.Core.Semantic.Metadata;
using Foundgine.Core.Semantic.Planning.Mutation;
using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.Query;
using Xunit;

namespace Foundgine.Extensions.GraphQL.HotChocolate.Tests;

/// <summary>
/// Acceptance tests for the mutation type/semantic boundary.
/// The adapter consumes GraphQL input/output structure and emits only semantic
/// mutation contracts; no GraphQL type objects cross into Planning/Execution.
/// </summary>
public sealed class MutationTypeSemanticsTests
{
    [Fact]
    public void CustomerInput_variable_maps_to_semantic_fields_and_nested_accounts()
    {
        var (model, registry) = BuildCustomerAccount();
        var adapter = new HotChocolateMutationAdapter(model, registry);

        var intent = adapter.Adapt("""
                                   mutation CreateCustomer($input: CustomerInput!) {
                                     createCustomer(input: $input) {
                                       id
                                       name
                                       accounts { id name }
                                     }
                                   }
                                   """, new Dictionary<string, object?>
        {
            ["input"] = new Dictionary<string, object?>
            {
                ["name"] = "Ada",
                ["accounts"] = new[]
                {
                    new Dictionary<string, object?> { ["name"] = "Checking" }
                }
            }
        });

        var root = Assert.IsType<MutationIntent>(intent.Mutation);
        Assert.Equal("Ada", Assert.Single(root.Fields).Value);
        Assert.Equal([new FieldId(1), new FieldId(2)], root.ReturnFields);

        var child = Assert.Single(intent.Children);
        var account = Assert.IsType<MutationIntent>(child.Mutation.Mutation);
        Assert.Equal("Checking", Assert.Single(account.Fields).Value);
        Assert.Equal([new FieldId(1), new FieldId(3)], account.ReturnFields);
    }

    [Fact]
    public void CustomerWhereInput_variable_maps_to_semantic_filter()
    {
        var (model, registry) = BuildCustomer();
        var adapter = new HotChocolateMutationAdapter(model, registry);

        var intent = adapter.Adapt("""
                                   mutation UpdateCustomer($input: CustomerInput!, $where: CustomerWhereInput!) {
                                     updateCustomer(input: $input, where: $where) { id name }
                                   }
                                   """, new Dictionary<string, object?>
        {
            ["input"] = new Dictionary<string, object?> { ["name"] = "Grace" },
            ["where"] = new Dictionary<string, object?>
            {
                ["id"] = new Dictionary<string, object?> { ["eq"] = 7L }
            }
        });

        var mutation = Assert.IsType<MutationIntent>(intent.Mutation);
        Assert.Equal("Grace", Assert.Single(mutation.Fields).Value);
        var filter = Assert.IsType<SemanticFieldFilter>(mutation.Filter);
        Assert.Equal(new FieldId(1), filter.Field);
        Assert.Equal(7L, filter.Value);
    }

    [Fact]
    public void Generated_identity_is_output_only_and_is_not_required_in_input()
    {
        var (model, registry) = BuildCustomer();
        var adapter = new HotChocolateMutationAdapter(model, registry);

        var adapted = adapter.AdaptWithResultShape("""
                                                   mutation CreateCustomer($input: CustomerInput!) {
                                                     createCustomer(input: $input) {
                                                       id
                                                       name
                                                     }
                                                   }
                                                   """, new Dictionary<string, object?>
        {
            ["input"] = new Dictionary<string, object?> { ["name"] = "Ada" }
        });

        var mutation = Assert.IsType<MutationIntent>(adapted.Intent.Mutation);
        Assert.DoesNotContain(mutation.Fields, x => x.ColumnId == new ColumnId(1));
        Assert.Equal([new FieldId(1), new FieldId(2)], mutation.ReturnFields);
        Assert.Equal(
            [
                new GraphQLMutationResultField(new FieldId(1), "id"),
                new GraphQLMutationResultField(new FieldId(2), "name")
            ],
            adapted.ResultShape.Fields);
    }

    [Fact]
    public void Nested_input_and_output_types_remain_graphql_boundary_concepts()
    {
        var (model, registry) = BuildCustomerAccount();
        var adapter = new HotChocolateMutationAdapter(model, registry);

        var adapted = adapter.AdaptWithResultShape("""
                                                   mutation CreateCustomer($input: CustomerInput!) {
                                                     createCustomer(input: $input) {
                                                       customerId: id
                                                       accounts {
                                                         accountId: id
                                                         accountName: name
                                                       }
                                                     }
                                                   }
                                                   """, new Dictionary<string, object?>
        {
            ["input"] = new Dictionary<string, object?>
            {
                ["name"] = "Ada",
                ["accounts"] = new[]
                {
                    new Dictionary<string, object?> { ["name"] = "Checking" }
                }
            }
        });

        Assert.Equal([new FieldId(1)],
            Assert.IsType<MutationIntent>(adapted.Intent.Mutation).ReturnFields);

        var relationship = Assert.Single(adapted.ResultShape.Relationships);
        Assert.Equal("accounts", relationship.ResponseName);
        Assert.Equal(
            [
                new GraphQLMutationResultField(new FieldId(1), "accountId"),
                new GraphQLMutationResultField(new FieldId(3), "accountName")
            ],
            relationship.Shape.Fields);

        Assert.Equal(new RelationshipId(10), relationship.Relationship);
    }

    private static (SemanticModel Model, MetadataRegistry Registry) BuildCustomer()
    {
        var customer = new EntityId(1);
        var registry = new MetadataRegistry();
        registry.Register(new EntityMetadata(customer, "Customer",
            [new ColumnMetadata(new ColumnId(1), "Id"), new ColumnMetadata(new ColumnId(2), "Name")],
            Fields:
            [
                new FieldMetadata(new FieldId(1), "Id", typeof(long), new ColumnReference(customer, new ColumnId(1))),
                new FieldMetadata(new FieldId(2), "Name", typeof(string),
                    new ColumnReference(customer, new ColumnId(2)))
            ], PrimaryKey: new ColumnReference(customer, new ColumnId(1))));

        var model = new SemanticModelBuilder()
            .Entity(customer, "Customer", e => e
                .Identity(new FieldId(1), "Id")
                .Field(new FieldId(2), "Name", typeof(string)))
            .Build();

        return (model, registry);
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