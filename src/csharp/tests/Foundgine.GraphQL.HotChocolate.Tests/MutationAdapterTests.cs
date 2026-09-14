using Foundgine.Extensions.GraphQL.HotChocolate;
using Foundgine.Core.Semantic.Metadata;
using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Planning.Mutation;
using Foundgine.Core.Semantic;
using Xunit;

namespace Foundgine.Extensions.GraphQL.HotChocolate.Tests;

public sealed class MutationAdapterTests
{
    [Fact]
    public void Create_mutation_translates_to_mutation_intent_and_return_fields()
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

        var intent = new HotChocolateMutationAdapter(model, registry).Adapt("""
                                                                            mutation {
                                                                              createCustomer(input: { name: "Ada" }) { id name }
                                                                            }
                                                                            """);

        var mutation = Assert.IsType<MutationIntent>(intent.Mutation);
        Assert.Equal(MutationKind.Create, mutation.Kind);
        Assert.Equal("Ada", mutation.Fields.Single().Value);
        Assert.Equal(new[] { new FieldId(1), new FieldId(2) }, mutation.ReturnFields);
    }

    [Fact]
    public void Update_requires_where_and_translates_filter()
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

        var intent = new HotChocolateMutationAdapter(model, registry).Adapt("""
                                                                            mutation {
                                                                              updateCustomer(input: { name: "Grace" }, where: { id: { eq: 1 } }) { id name }
                                                                            }
                                                                            """);

        var mutation = Assert.IsType<MutationIntent>(intent.Mutation);
        Assert.Equal(MutationKind.Update, mutation.Kind);
        Assert.NotNull(mutation.Filter);
    }

    [Fact]
    public void Nested_create_mutation_becomes_nested_mutation_intent()
    {
        var customer = new EntityId(1);
        var account = new EntityId(2);
        var customerId = new ColumnId(1);
        var accountId = new ColumnId(1);
        var accountCustomerId = new ColumnId(2);
        var accountName = new ColumnId(3);
        var customerName = new ColumnId(2);
        var relationshipId = new RelationshipId(1);

        var registry = new MetadataRegistry();
        registry.Register(new EntityMetadata(customer, "Customer",
            [new ColumnMetadata(customerId, "Id"), new ColumnMetadata(customerName, "Name")],
            Fields:
            [
                new FieldMetadata(new FieldId(1), "Id", typeof(long), new ColumnReference(customer, customerId)),
                new FieldMetadata(new FieldId(2), "Name", typeof(string), new ColumnReference(customer, customerName))
            ], PrimaryKey: new ColumnReference(customer, customerId)));
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
            ], PrimaryKey: new ColumnReference(account, accountId)));
        registry.Register(new RelationshipMetadata(
            relationshipId, customer, account, "Accounts",
            new ColumnReference(customer, customerId),
            new ColumnReference(account, accountCustomerId)));

        var model = new SemanticModelBuilder()
            .Entity(customer, "Customer", e => e.Identity(new FieldId(1), "Id")
                .Field(new FieldId(2), "Name", typeof(string))
                .Relationship(relationshipId, "Accounts", account, RelationshipCardinality.Many))
            .Entity(account, "Account",
                e => e.Identity(new FieldId(1), "Id").Field(new FieldId(2), "CustomerId", typeof(long))
                    .Field(new FieldId(3), "Name", typeof(string)))
            .Build();

        var intent = new HotChocolateMutationAdapter(model, registry).Adapt("""
                                                                            mutation {
                                                                              createCustomer(input: { name: "Ada", accounts: [{ name: "Checking" }] }) { id name }
                                                                            }
                                                                            """);

        Assert.Single(intent.Children);
        Assert.Equal(account, intent.Children[0].Mutation.Mutation.Entity);
        Assert.Equal("Checking", Assert.Single(intent.Children[0].Mutation.Mutation.Fields).Value);
    }
}