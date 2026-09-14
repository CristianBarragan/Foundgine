using Foundgine.Core.Abstractions;
using Foundgine.Extensions.GraphQL.HotChocolate;
using Foundgine.Core.Semantic.Metadata;
using Foundgine.Core.Semantic.Planning.Mutation;
using Foundgine.Core.Semantic;
using Xunit;

namespace Foundgine.Extensions.GraphQL.HotChocolate.Tests;

/// <summary>
/// GraphQL features must compose at the adapter boundary without introducing
/// GraphQL concepts into provider-neutral requests or mutation intents.
/// </summary>
public sealed class IntegrationHardeningTests
{
    [Fact]
    public void Query_variables_fragments_aliases_directives_and_operation_selection_compose()
    {
        var model = BuildCustomerModel();
        var adapter = new HotChocolateSemanticAdapter(model);

        var result = adapter.AdaptResultShape("""
                                              query Ignored {
                                                customer { id }
                                              }

                                              query CustomerView($showName: Boolean!) {
                                                customer {
                                                  ...CustomerFields
                                                }
                                              }

                                              fragment CustomerFields on Customer {
                                                customerId: id
                                                displayName: name @include(if: $showName)
                                              }
                                              """,
            new Dictionary<string, object?> { ["showName"] = true },
            "CustomerView");

        Assert.Equal("Customer", model.Get(result.Request.Root).Name);
        Assert.Contains(result.Request.Selections, x => x.Field is not null);
        Assert.Contains(result.Result.Fields, x => x.Alias == "customerId");
        Assert.Contains(result.Result.Fields, x => x.Alias == "displayName");
    }

    [Fact]
    public void Mutation_variables_fragments_aliases_directives_and_operation_selection_compose()
    {
        var (model, metadata) = BuildCustomer();
        var adapter = new HotChocolateMutationAdapter(model, metadata);

        var result = adapter.AdaptResultShape("""
                                              mutation Ignored($input: CustomerInput!) {
                                                createCustomer(input: $input) { id }
                                              }

                                              mutation CreateCustomer($input: CustomerInput!, $showName: Boolean!) {
                                                createCustomer(input: $input) {
                                                  ...CustomerFields
                                                }
                                              }

                                              fragment CustomerFields on Customer {
                                                customerId: id
                                                displayName: name @include(if: $showName)
                                              }
                                              """,
            new Dictionary<string, object?>
            {
                ["input"] = new Dictionary<string, object?> { ["name"] = "Ada" },
                ["showName"] = true
            },
            "CreateCustomer");

        var mutation = Assert.IsType<MutationIntent>(result.Intent.Mutation);
        Assert.Equal("Ada", Assert.Single(mutation.Fields).Value);
        Assert.Contains(result.Result.Fields, x => x.Alias == "customerId");
        Assert.Contains(result.Result.Fields, x => x.Alias == "displayName");
    }

    [Fact]
    public void TryAdapt_preserves_expected_client_error_semantics()
    {
        var (model, metadata) = BuildCustomer();
        var result = new HotChocolateMutationAdapter(model, metadata).TryAdapt("""
                                                                               mutation CreateCustomer($input: CustomerInput!) {
                                                                                 createCustomer(input: $input) { id }
                                                                               }
                                                                               """);

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
        Assert.Equal(GraphQLAdapterErrorCode.BadUserInput, result.Error!.Code);
    }

    [Fact]
    public void Core_mutation_intent_contains_no_graphql_projection_data()
    {
        var (model, metadata) = BuildCustomer();
        var intent = new HotChocolateMutationAdapter(model, metadata).AdaptResultShape("""
                mutation CreateCustomer($input: CustomerInput!) {
                  createCustomer(input: $input) {
                    customerId: id
                    displayName: name
                  }
                }
                """,
            new Dictionary<string, object?>
            {
                ["input"] = new Dictionary<string, object?> { ["name"] = "Ada" }
            });

        var mutation = Assert.IsType<MutationIntent>(intent.Intent.Mutation);

        Assert.Equal("Ada", Assert.Single(mutation.Fields).Value);
        Assert.Equal(
            new[] { new FieldId(1), new FieldId(2) },
            mutation.ReturnFields);
    }

    private static SemanticModel BuildCustomerModel()
    {
        var customer = new EntityId(1);
        return new SemanticModelBuilder()
            .Entity(customer, "Customer", e => e
                .Identity(new FieldId(1), "Id")
                .Field(new FieldId(2), "Name", typeof(string)))
            .Build();
    }

    private static (SemanticModel Model, MetadataRegistry Metadata) BuildCustomer()
    {
        var customer = new EntityId(1);
        var registry = new MetadataRegistry();

        registry.Register(new EntityMetadata(customer, "Customer",
            [new ColumnMetadata(new ColumnId(1), "Id"), new ColumnMetadata(new ColumnId(2), "Name")],
            Fields:
            [
                new FieldMetadata(new FieldId(1), "Id", typeof(long),
                    new ColumnReference(customer, new ColumnId(1))),
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