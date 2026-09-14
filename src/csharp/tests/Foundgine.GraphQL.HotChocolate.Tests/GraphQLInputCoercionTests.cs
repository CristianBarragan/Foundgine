using Foundgine.Core.Abstractions;
using Foundgine.Extensions.GraphQL.HotChocolate;
using Foundgine.Core.Semantic.Metadata;
using Foundgine.Core.Semantic.Planning.Mutation;
using Foundgine.Core.Semantic;
using Xunit;

namespace Foundgine.Extensions.GraphQL.HotChocolate.Tests;

public sealed class GraphQLInputCoercionTests
{
    [Fact]
    public void NonNullVariable_IsRequired()
    {
        var (model, metadata) = BuildCustomer();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new HotChocolateMutationAdapter(model, metadata).Adapt("""
                                                                   mutation CreateCustomer($input: CustomerInput!) {
                                                                     createCustomer(input: $input) { id }
                                                                   }
                                                                   """));

        Assert.Contains("$input", ex.Message);
        Assert.Contains("non-null", ex.Message);
    }

    [Fact]
    public void NullableVariable_MissingValueResolvesToNull()
    {
        var (model, metadata) = BuildCustomer();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new HotChocolateMutationAdapter(model, metadata).Adapt("""
                                                                   mutation CreateCustomer($input: CustomerInput) {
                                                                     createCustomer(input: $input) { id }
                                                                   }
                                                                   """));

        Assert.Contains("input", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DefaultInputObject_IsCoerced()
    {
        var (model, metadata) = BuildCustomer();

        var intent = new HotChocolateMutationAdapter(model, metadata).Adapt("""
                                                                            mutation CreateCustomer($input: CustomerInput = { name: "Ada" }) {
                                                                              createCustomer(input: $input) { id name }
                                                                            }
                                                                            """);

        var mutation = Assert.IsType<MutationIntent>(intent.Mutation);
        Assert.Equal("Ada", Assert.Single(mutation.Fields).Value);
    }

    [Fact]
    public void ScalarVariable_IsCoercedToDeclaredType()
    {
        var (model, metadata) = BuildCustomer();

        var intent = new HotChocolateMutationAdapter(model, metadata).Adapt("""
                                                                            mutation CreateCustomer($name: String!) {
                                                                              createCustomer(input: { name: $name }) { id name }
                                                                            }
                                                                            """,
            new Dictionary<string, object?> { ["name"] = "Ada" });

        var mutation = Assert.IsType<MutationIntent>(intent.Mutation);
        Assert.Equal("Ada", Assert.Single(mutation.Fields).Value);
    }

    [Fact]
    public void WrongScalarType_IsRejected()
    {
        var (model, metadata) = BuildCustomer();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new HotChocolateMutationAdapter(model, metadata).Adapt("""
                                                                   mutation CreateCustomer($name: String!) {
                                                                     createCustomer(input: { name: $name }) { id name }
                                                                   }
                                                                   """,
                new Dictionary<string, object?> { ["name"] = 123 }));

        Assert.Contains("String", ex.Message);
    }

    [Fact]
    public void NullForNonNullVariable_IsRejected()
    {
        var (model, metadata) = BuildCustomer();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new HotChocolateMutationAdapter(model, metadata).Adapt("""
                                                                   mutation CreateCustomer($name: String!) {
                                                                     createCustomer(input: { name: $name }) { id name }
                                                                   }
                                                                   """,
                new Dictionary<string, object?> { ["name"] = null }));

        Assert.Contains("cannot be null", ex.Message);
    }

    [Fact]
    public void ListVariable_AcceptsSingletonValue()
    {
        var (model, metadata) = BuildCustomer();

        // The adapter-level coercion accepts GraphQL's singleton-to-list input coercion.
        var intent = new HotChocolateMutationAdapter(model, metadata).Adapt("""
                                                                            mutation UpsertCustomer($names: [String!]!) {
                                                                              upsertCustomer(input: { name: "Ada" }, onConflict: $names) { id name }
                                                                            }
                                                                            """,
            new Dictionary<string, object?> { ["names"] = "Name" });

        var mutation = Assert.IsType<UpsertIntent>(intent.Mutation);
        Assert.Equal(new[] { new ColumnId(2) }, mutation.ConflictColumns);
    }

    [Fact]
    public void ExtraSuppliedVariable_IsIgnored()
    {
        var (model, metadata) = BuildCustomer();

        var intent = new HotChocolateMutationAdapter(model, metadata).Adapt("""
                                                                            mutation CreateCustomer($name: String!) {
                                                                              createCustomer(input: { name: $name }) { id name }
                                                                            }
                                                                            """, new Dictionary<string, object?>
        {
            ["name"] = "Ada",
            ["unused"] = true
        });

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
            .Entity(customer, "Customer", e => e
                .Identity(new FieldId(1), "Id")
                .Field(new FieldId(2), "Name", typeof(string)))
            .Build();

        return (model, registry);
    }
}