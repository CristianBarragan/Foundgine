using Foundgine.Core.Abstractions;
using Foundgine.Extensions.GraphQL.HotChocolate;
using Foundgine.Core.Semantic.Metadata;
using Foundgine.Core.Semantic.Planning.Mutation;
using Foundgine.Core.Semantic;
using Xunit;

namespace Foundgine.Extensions.GraphQL.HotChocolate.Tests;

public sealed class MutationVariableCoercionTests
{
    [Fact]
    public void Wrong_scalar_variable_type_is_rejected_before_translation()
    {
        var (model, registry) = BuildCustomer();
        var adapter = new HotChocolateMutationAdapter(model, registry);

        var ex = Assert.Throws<InvalidOperationException>(() => adapter.Adapt("""
                                                                              mutation CreateCustomer($input: CustomerInput!) {
                                                                                createCustomer(input: $input) { id name }
                                                                              }
                                                                              """, new Dictionary<string, object?>
        {
            ["input"] = "Ada"
        }));

        Assert.Contains("input", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Non_null_variable_cannot_be_null()
    {
        var (model, registry) = BuildCustomer();
        var adapter = new HotChocolateMutationAdapter(model, registry);

        var ex = Assert.Throws<InvalidOperationException>(() => adapter.Adapt("""
                                                                              mutation CreateCustomer($input: CustomerInput!) {
                                                                                createCustomer(input: $input) { id }
                                                                              }
                                                                              """, new Dictionary<string, object?>
        {
            ["input"] = null
        }));

        Assert.Contains("cannot be null", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void List_variable_requires_a_runtime_list()
    {
        var (model, registry) = BuildCustomer();
        var adapter = new HotChocolateMutationAdapter(model, registry);

        var ex = Assert.Throws<InvalidOperationException>(() => adapter.Adapt("""
                                                                              mutation CreateCustomers($inputs: [CustomerInput!]!) {
                                                                                createCustomer(input: $inputs) { id }
                                                                              }
                                                                              """, new Dictionary<string, object?>
        {
            ["inputs"] = new Dictionary<string, object?> { ["name"] = "Ada" }
        }));

        Assert.Contains("list", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Variable_default_value_is_used_when_runtime_value_is_omitted()
    {
        var (model, registry) = BuildCustomer();
        var adapter = new HotChocolateMutationAdapter(model, registry);

        var intent = adapter.Adapt("""
                                   mutation CreateCustomer($input: CustomerInput! = { name: "Ada" }) {
                                     createCustomer(input: $input) { id name }
                                   }
                                   """);

        var mutation = Assert.IsType<MutationIntent>(intent.Mutation);
        Assert.Equal("Ada", Assert.Single(mutation.Fields).Value);
    }

    [Fact]
    public void Extra_runtime_variables_are_ignored_like_graphql_variable_coercion()
    {
        var (model, registry) = BuildCustomer();
        var adapter = new HotChocolateMutationAdapter(model, registry);

        var intent = adapter.Adapt("""
                                   mutation CreateCustomer($input: CustomerInput!) {
                                     createCustomer(input: $input) { id name }
                                   }
                                   """, new Dictionary<string, object?>
        {
            ["input"] = new Dictionary<string, object?> { ["name"] = "Ada" },
            ["unused"] = 123
        });

        var mutation = Assert.IsType<MutationIntent>(intent.Mutation);
        Assert.Equal("Ada", Assert.Single(mutation.Fields).Value);
    }

    [Fact]
    public void Input_object_field_type_is_checked_against_semantic_field_type()
    {
        var (model, registry) = BuildCustomer();
        var adapter = new HotChocolateMutationAdapter(model, registry);

        var ex = Assert.Throws<InvalidOperationException>(() => adapter.Adapt("""
                                                                              mutation CreateCustomer($input: CustomerInput!) {
                                                                                createCustomer(input: $input) { id name }
                                                                              }
                                                                              """, new Dictionary<string, object?>
        {
            ["input"] = new Dictionary<string, object?> { ["name"] = 123 }
        }));

        Assert.Contains("expects 'String'", ex.Message);
    }

    [Fact]
    public void Guid_string_is_coerced_to_a_clr_guid_before_sql_execution()
    {
        var (model, registry) = BuildGuidCustomer();
        var adapter = new HotChocolateMutationAdapter(model, registry);
        var expected = Guid.Parse("550e8400-e29b-41d4-a716-446655440000");

        var intent = adapter.Adapt("""
                                   mutation CreateCustomer($input: CustomerInput!) {
                                     createCustomer(input: $input) { id customerKey }
                                   }
                                   """, new Dictionary<string, object?>
        {
            ["input"] = new Dictionary<string, object?>
            {
                ["customerKey"] = expected.ToString()
            }
        });

        var mutation = Assert.IsType<MutationIntent>(intent.Mutation);
        var field = Assert.Single(mutation.Fields);

        Assert.Equal(expected, field.Value);
        Assert.IsType<Guid>(field.Value);
    }

    [Fact]
    public void Nullable_guid_field_accepts_null()
    {
        var (model, registry) = BuildNullableGuidCustomer();
        var adapter = new HotChocolateMutationAdapter(model, registry);

        var intent = adapter.Adapt("""
                                   mutation CreateCustomer($input: CustomerInput!) {
                                     createCustomer(input: $input) { id customerKey }
                                   }
                                   """, new Dictionary<string, object?>
        {
            ["input"] = new Dictionary<string, object?>
            {
                ["customerKey"] = null
            }
        });

        var mutation = Assert.IsType<MutationIntent>(intent.Mutation);
        Assert.Null(Assert.Single(mutation.Fields).Value);
    }

    [Fact]
    public void Non_nullable_semantic_field_cannot_receive_null()
    {
        var (model, registry) = BuildCustomer();
        var adapter = new HotChocolateMutationAdapter(model, registry);

        var ex = Assert.Throws<InvalidOperationException>(() => adapter.Adapt("""
                                                                              mutation CreateCustomer($input: CustomerInput!) {
                                                                                createCustomer(input: $input) { id name }
                                                                              }
                                                                              """, new Dictionary<string, object?>
        {
            ["input"] = new Dictionary<string, object?> { ["id"] = null }
        }));

        Assert.Contains("cannot be null", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static (SemanticModel Model, MetadataRegistry Registry) BuildGuidCustomer()
    {
        var customer = new EntityId(1);
        var registry = new MetadataRegistry();
        registry.Register(new EntityMetadata(customer, "Customer",
            [new ColumnMetadata(new ColumnId(1), "Id"), new ColumnMetadata(new ColumnId(2), "CustomerKey")],
            Fields:
            [
                new FieldMetadata(new FieldId(1), "Id", typeof(long), new ColumnReference(customer, new ColumnId(1))),
                new FieldMetadata(new FieldId(2), "CustomerKey", typeof(Guid),
                    new ColumnReference(customer, new ColumnId(2)))
            ],
            PrimaryKey: new ColumnReference(customer, new ColumnId(1))));

        var model = new SemanticModelBuilder()
            .Entity(customer, "Customer", e => e
                .Identity(new FieldId(1), "Id")
                .Field(new FieldId(2), "CustomerKey", typeof(Guid)))
            .Build();

        return (model, registry);
    }

    private static (SemanticModel Model, MetadataRegistry Registry) BuildNullableGuidCustomer()
    {
        var customer = new EntityId(1);
        var registry = new MetadataRegistry();
        registry.Register(new EntityMetadata(customer, "Customer",
            [new ColumnMetadata(new ColumnId(1), "Id"), new ColumnMetadata(new ColumnId(2), "CustomerKey")],
            Fields:
            [
                new FieldMetadata(new FieldId(1), "Id", typeof(long), new ColumnReference(customer, new ColumnId(1))),
                new FieldMetadata(new FieldId(2), "CustomerKey", typeof(Guid?),
                    new ColumnReference(customer, new ColumnId(2)))
            ],
            PrimaryKey: new ColumnReference(customer, new ColumnId(1))));

        var model = new SemanticModelBuilder()
            .Entity(customer, "Customer", e => e
                .Identity(new FieldId(1), "Id")
                .Field(new FieldId(2), "CustomerKey", typeof(Guid?)))
            .Build();

        return (model, registry);
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