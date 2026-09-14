using Foundgine.Core.Abstractions;
using Foundgine.Extensions.GraphQL.HotChocolate;
using Foundgine.Core.Semantic.Metadata;
using Foundgine.Core.Semantic.Planning.Mutation;
using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.Query;
using Xunit;

namespace Foundgine.Extensions.GraphQL.HotChocolate.Tests;

public sealed class MutationVariableTests
{
    [Fact]
    public void Object_variable_produces_the_same_mutation_values_as_inline_input()
    {
        var (model, metadata) = BuildCustomer();
        var adapter = new HotChocolateMutationAdapter(model, metadata);

        var inline = adapter.Adapt("""
                                   mutation {
                                     createCustomer(input: { name: "Ada" }) { id name }
                                   }
                                   """);

        var variable = adapter.Adapt("""
                                     mutation CreateCustomer($input: CustomerInput!) {
                                       createCustomer(input: $input) { id name }
                                     }
                                     """, new Dictionary<string, object?>
        {
            ["input"] = new Dictionary<string, object?> { ["name"] = "Ada" }
        });

        var inlineMutation = Assert.IsType<MutationIntent>(inline.Mutation);
        var variableMutation = Assert.IsType<MutationIntent>(variable.Mutation);
        Assert.Equal(inlineMutation.Fields.Single().Value, variableMutation.Fields.Single().Value);
        Assert.Equal(inlineMutation.ReturnFields, variableMutation.ReturnFields);
    }

    [Fact]
    public void Scalar_variable_can_be_used_inside_input_and_where()
    {
        var (model, metadata) = BuildCustomer();

        var intent = new HotChocolateMutationAdapter(model, metadata).Adapt("""
                                                                            mutation UpdateCustomer($name: String!, $id: Long!) {
                                                                              updateCustomer(input: { name: $name }, where: { id: { eq: $id } }) { id name }
                                                                            }
                                                                            """, new Dictionary<string, object?>
        {
            ["name"] = "Grace",
            ["id"] = 42L
        });

        var mutation = Assert.IsType<MutationIntent>(intent.Mutation);
        Assert.Equal("Grace", Assert.Single(mutation.Fields).Value);
        var filter = Assert.IsType<SemanticFieldFilter>(mutation.Filter);
        Assert.Equal(42L, filter.Value);
    }

    [Fact]
    public void Nested_object_variable_is_resolved_at_the_graphql_boundary()
    {
        var customer = new EntityId(1);
        var account = new EntityId(2);
        var relationship = new RelationshipId(1);

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
            [new ColumnMetadata(new ColumnId(1), "Id"), new ColumnMetadata(new ColumnId(2), "Name")],
            Fields:
            [
                new FieldMetadata(new FieldId(1), "Id", typeof(long), new ColumnReference(account, new ColumnId(1))),
                new FieldMetadata(new FieldId(2), "Name", typeof(string), new ColumnReference(account, new ColumnId(2)))
            ], PrimaryKey: new ColumnReference(account, new ColumnId(1))));
        registry.Register(new RelationshipMetadata(
            relationship, customer, account, "Accounts",
            new ColumnReference(customer, new ColumnId(1)),
            new ColumnReference(account, new ColumnId(1))));

        var model = new SemanticModelBuilder()
            .Entity(customer, "Customer", e => e.Identity(new FieldId(1), "Id")
                .Field(new FieldId(2), "Name", typeof(string))
                .Relationship(relationship, "Accounts", account, RelationshipCardinality.Many))
            .Entity(account, "Account",
                e => e.Identity(new FieldId(1), "Id").Field(new FieldId(2), "Name", typeof(string)))
            .Build();

        var intent = new HotChocolateMutationAdapter(model, registry).Adapt("""
                                                                            mutation CreateCustomer($input: CustomerInput!) {
                                                                              createCustomer(input: $input) { id name }
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

        var child = Assert.Single(intent.Children);
        var childMutation = Assert.IsType<MutationIntent>(child.Mutation.Mutation);
        Assert.Equal("Checking", Assert.Single(childMutation.Fields).Value);
    }

    [Fact]
    public void Missing_variable_is_rejected()
    {
        var (model, metadata) = BuildCustomer();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new HotChocolateMutationAdapter(model, metadata).Adapt("""
                                                                   mutation CreateCustomer($input: CustomerInput!) {
                                                                     createCustomer(input: $input) { id name }
                                                                   }
                                                                   """));

        Assert.Contains("$input", exception.Message);
    }

    [Fact]
    public void Variable_default_value_is_used_when_runtime_value_is_absent()
    {
        var (model, metadata) = BuildCustomer();

        var intent = new HotChocolateMutationAdapter(model, metadata).Adapt("""
                                                                            mutation CreateCustomer($name: String = "Ada") {
                                                                              createCustomer(input: { name: $name }) { id name }
                                                                            }
                                                                            """);

        var mutation = Assert.IsType<MutationIntent>(intent.Mutation);
        Assert.Equal("Ada", Assert.Single(mutation.Fields).Value);
    }

    private static (SemanticModel Model, MetadataRegistry Metadata) BuildCustomer()
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
            ],
            PrimaryKey: new ColumnReference(customer, new ColumnId(1))));

        var model = new SemanticModelBuilder()
            .Entity(customer, "Customer",
                e => e.Identity(new FieldId(1), "Id").Field(new FieldId(2), "Name", typeof(string)))
            .Build();

        return (model, registry);
    }
}