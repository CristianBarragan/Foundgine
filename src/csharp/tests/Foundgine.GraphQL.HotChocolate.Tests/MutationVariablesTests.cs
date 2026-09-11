using Foundgine.Core.Abstractions;
using Foundgine.Extensions.GraphQL.HotChocolate;
using Foundgine.Core.Semantic.Metadata;
using Foundgine.Core.Semantic.Planning.Mutation;
using Foundgine.Core.Semantic;
using Xunit;

namespace Foundgine.Extensions.GraphQL.HotChocolate.Tests;

public sealed class MutationVariablesTests
{
    [Fact]
    public void Create_mutation_accepts_runtime_input_variable()
    {
        var customer = new EntityId(1);
        var registry = BuildRegistry(customer);
        var model = BuildModel(customer);
        var adapter = new HotChocolateMutationAdapter(model, registry);

        var intent = adapter.Adapt("""
                                   mutation CreateCustomer($input: CustomerInput!) {
                                     createCustomer(input: $input) { id name }
                                   }
                                   """, new Dictionary<string, object?>
        {
            ["input"] = new Dictionary<string, object?>
            {
                ["name"] = "Ada"
            }
        });

        var mutation = Assert.IsType<MutationIntent>(intent.Mutation);
        Assert.Equal(MutationKind.Create, mutation.Kind);
        Assert.Equal("Ada", Assert.Single(mutation.Fields).Value);
        Assert.Equal(new[] { new FieldId(1), new FieldId(2) }, mutation.ReturnFields);
    }

    [Fact]
    public void Update_mutation_accepts_variable_for_input_and_where()
    {
        var customer = new EntityId(1);
        var registry = BuildRegistry(customer);
        var model = BuildModel(customer);
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
                ["id"] = new Dictionary<string, object?> { ["eq"] = 1L }
            }
        });

        var mutation = Assert.IsType<MutationIntent>(intent.Mutation);
        Assert.Equal(MutationKind.Update, mutation.Kind);
        Assert.Equal("Grace", Assert.Single(mutation.Fields).Value);
        Assert.NotNull(mutation.Filter);
    }

    [Fact]
    public void Nested_variable_input_is_translated_like_inline_input()
    {
        var customer = new EntityId(1);
        var account = new EntityId(2);
        var registry = BuildNestedRegistry(customer, account);
        var model = BuildNestedModel(customer, account);
        var adapter = new HotChocolateMutationAdapter(model, registry);

        var intent = adapter.Adapt("""
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

        Assert.Single(intent.Children);
        Assert.Equal(account, intent.Children[0].Mutation.Mutation.Entity);
        Assert.Equal("Checking", Assert.Single(intent.Children[0].Mutation.Mutation.Fields).Value);
    }

    [Fact]
    public void Declared_variable_without_runtime_value_is_rejected()
    {
        var customer = new EntityId(1);
        var registry = BuildRegistry(customer);
        var model = BuildModel(customer);
        var adapter = new HotChocolateMutationAdapter(model, registry);

        var ex = Assert.Throws<InvalidOperationException>(() => adapter.Adapt("""
                                                                              mutation CreateCustomer($input: CustomerInput!) {
                                                                                createCustomer(input: $input) { id }
                                                                              }
                                                                              """, new Dictionary<string, object?>()));

        Assert.Contains("$input", ex.Message);
    }

    [Fact]
    public void Variable_syntax_without_runtime_values_is_rejected()
    {
        var customer = new EntityId(1);
        var registry = BuildRegistry(customer);
        var model = BuildModel(customer);
        var adapter = new HotChocolateMutationAdapter(model, registry);

        var ex = Assert.Throws<InvalidOperationException>(() => adapter.Adapt("""
                                                                              mutation CreateCustomer($input: CustomerInput!) {
                                                                                createCustomer(input: $input) { id }
                                                                              }
                                                                              """));

        Assert.Contains("runtime variable-value dictionary", ex.Message);
    }

    private static SemanticModel BuildModel(EntityId customer) => new SemanticModelBuilder()
        .Entity(customer, "Customer", e => e
            .Identity(new FieldId(1), "Id")
            .Field(new FieldId(2), "Name", typeof(string)))
        .Build();

    private static MetadataRegistry BuildRegistry(EntityId customer)
    {
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
        return registry;
    }

    private static SemanticModel BuildNestedModel(EntityId customer, EntityId account)
    {
        var relationship = new RelationshipId(1);
        return new SemanticModelBuilder()
            .Entity(customer, "Customer", e => e
                .Identity(new FieldId(1), "Id")
                .Field(new FieldId(2), "Name", typeof(string))
                .Relationship(relationship, "Accounts", account, RelationshipCardinality.Many))
            .Entity(account, "Account", e => e
                .Identity(new FieldId(1), "Id")
                .Field(new FieldId(2), "CustomerId", typeof(long))
                .Field(new FieldId(3), "Name", typeof(string)))
            .Build();
    }

    private static MetadataRegistry BuildNestedRegistry(EntityId customer, EntityId account)
    {
        var registry = new MetadataRegistry();
        var customerId = new ColumnId(1);
        var customerName = new ColumnId(2);
        var accountId = new ColumnId(1);
        var accountCustomerId = new ColumnId(2);
        var accountName = new ColumnId(3);

        registry.Register(new EntityMetadata(customer, "Customer",
            [new ColumnMetadata(customerId, "Id"), new ColumnMetadata(customerName, "Name")],
            Fields:
            [
                new FieldMetadata(new FieldId(1), "Id", typeof(long), new ColumnReference(customer, customerId)),
                new FieldMetadata(new FieldId(2), "Name", typeof(string), new ColumnReference(customer, customerName))
            ],
            PrimaryKey: new ColumnReference(customer, customerId)));
        registry.Register(new EntityMetadata(account, "Account",
            [
                new ColumnMetadata(accountId, "Id"), new ColumnMetadata(accountCustomerId, "CustomerId"),
                new ColumnMetadata(accountName, "Name")
            ],
            Fields:
            [
                new FieldMetadata(new FieldId(1), "Id", typeof(long), new ColumnReference(account, accountId)),
                new FieldMetadata(new FieldId(2), "CustomerId", typeof(long),
                    new ColumnReference(account, accountCustomerId)),
                new FieldMetadata(new FieldId(3), "Name", typeof(string), new ColumnReference(account, accountName))
            ],
            PrimaryKey: new ColumnReference(account, accountId)));
        registry.Register(new RelationshipMetadata(
            new RelationshipId(1), customer, account, "Accounts",
            new ColumnReference(customer, customerId),
            new ColumnReference(account, accountCustomerId)));
        return registry;
    }
}