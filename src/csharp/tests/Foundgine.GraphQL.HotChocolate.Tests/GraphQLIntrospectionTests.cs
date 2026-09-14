using Foundgine.Core.Abstractions;
using Foundgine.Extensions.GraphQL.HotChocolate;
using Foundgine.Core.Semantic.Metadata;
using Foundgine.Core.Semantic;
using Xunit;

namespace Foundgine.Extensions.GraphQL.HotChocolate.Tests;

public sealed class GraphQLIntrospectionTests
{
    [Fact]
    public void Schema_descriptor_exposes_query_entities_relationships_and_mutations()
    {
        var customer = new EntityId(1);
        var account = new EntityId(2);
        var model = new SemanticModelBuilder()
            .Entity(customer, "Customer", e => e
                .Identity(new FieldId(1), "Id")
                .Field(new FieldId(2), "Name", typeof(string))
                .Relationship(new RelationshipId(1), "Accounts", account, RelationshipCardinality.Many))
            .Entity(account, "Account", e => e
                .Identity(new FieldId(1), "Id")
                .Field(new FieldId(2), "Balance", typeof(decimal)))
            .Build();

        var schema = new GraphQLSchemaAdapter(model).Build();

        Assert.Contains(schema.QueryTypes[0].Fields, x => x.Name == "customer" && x.Type == "Customer");
        Assert.Contains(schema.MutationTypes[0].Fields, x => x.Name == "createCustomer");
        Assert.Contains(schema.MutationTypes[0].Fields, x => x.Name == "updateCustomer");
        Assert.Contains(schema.MutationTypes[0].Fields, x => x.Name == "deleteCustomer");
        Assert.Contains(schema.MutationTypes[0].Fields, x => x.Name == "upsertCustomer");

        var customerType = Assert.Single(schema.QueryTypes[0].Fields, x => x.Name == "customer");
        Assert.Equal("Customer", customerType.Type);

        var input = Assert.Single(schema.InputTypes, x => x.Name == "CustomerInput");
        Assert.Contains(input.Fields, x => x.Name == "name" && x.Type == "String");

        var where = Assert.Single(schema.InputTypes, x => x.Name == "CustomerWhereInput");
        Assert.Contains(where.Fields, x => x.Name == "id" && x.Type == "Int");
    }

    [Fact]
    public void BuildSdl_contains_introspection_compatible_schema_vocabulary()
    {
        var customer = new EntityId(1);
        var model = new SemanticModelBuilder()
            .Entity(customer, "Customer", e => e
                .Identity(new FieldId(1), "Id")
                .Field(new FieldId(2), "Name", typeof(string)))
            .Build();

        var sdl = new GraphQLSchemaAdapter(model).BuildSdl();

        Assert.Contains("type Query", sdl);
        Assert.Contains("customer: Customer!", sdl);
        Assert.Contains("type Customer", sdl);
        Assert.Contains("id: ID!", sdl);
        Assert.Contains("name: String", sdl);
        Assert.Contains("type Mutation", sdl);
        Assert.Contains("createCustomer(input: CustomerInput!): Customer!", sdl);
        Assert.Contains("input CustomerInput", sdl);
        Assert.Contains("input CustomerWhereInput", sdl);
    }

    [Fact]
    public void Schema_generation_is_deterministic()
    {
        var model = new SemanticModelBuilder()
            .Entity(new EntityId(2), "Zebra", e => e.Identity(new FieldId(1), "Id"))
            .Entity(new EntityId(1), "Alpha", e => e.Identity(new FieldId(1), "Id"))
            .Build();

        var adapter = new GraphQLSchemaAdapter(model);

        Assert.Equal(adapter.BuildSdl(), adapter.BuildSdl());
        Assert.True(adapter.BuildSdl().IndexOf("alpha", StringComparison.Ordinal) <
                    adapter.BuildSdl().IndexOf("zebra", StringComparison.Ordinal));
    }
}
