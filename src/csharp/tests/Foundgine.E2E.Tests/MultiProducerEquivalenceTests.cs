using Foundgine.Core.Abstractions;
using Foundgine.Extensions.GraphQL.HotChocolate;
using Foundgine.Core.Serialization;
using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.Intent;
using Foundgine.Core.Semantic.Query;
using Xunit;

namespace Foundgine.E2E.Tests;

public sealed class MultiProducerEquivalenceTests
{
    [Fact]
    public void GraphQL_and_json_producers_compile_to_the_same_semantic_request()
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

        const string json = """
                            {
                              "rootEntity": "Customer",
                              "selections": [
                                { "field": "Id" },
                                { "field": "Name" },
                                { "relationship": "Accounts", "children": [
                                  { "field": "Id" },
                                  { "field": "Balance" }
                                ] }
                              ],
                              "filter": {
                                "kind": "field",
                                "field": "Name",
                                "operator": "Eq",
                                "value": "Alice"
                              },
                              "order": [
                                { "field": "Name", "direction": "Desc" }
                              ],
                              "limit": 10,
                              "offset": 2
                            }
                            """;

        const string graphql = """
                               query {
                                 customer(where: { name: { eq: "Alice" } }, order: { name: DESC }, first: 10, skip: 2) {
                                   id
                                   name
                                   accounts {
                                     id
                                     balance
                                   }
                                 }
                               }
                               """;

        var jsonIntent = new JsonReadIntentAdapter().Parse(json);
        var jsonRequest = new ReadIntentCompiler(model).Compile(jsonIntent);
        var graphqlRequest = new HotChocolateSemanticAdapter(model).Adapt(graphql);

        AssertSemanticRequestsEqual(jsonRequest, graphqlRequest);
    }

    [Fact]
    public void Equivalent_producers_preserve_the_same_semantic_identity_not_names()
    {
        var customer = new EntityId(10);
        var model = new SemanticModelBuilder()
            .Entity(customer, "Customer", e => e
                .Identity(new FieldId(100), "Id")
                .Field(new FieldId(101), "Name", typeof(string)))
            .Build();

        var json = new JsonReadIntentAdapter().Parse("""
                                                     {
                                                       "rootEntity": "Customer",
                                                       "selections": [ { "field": "Name" } ],
                                                       "filter": { "kind": "field", "field": "Name", "operator": "Eq", "value": "Alice" }
                                                     }
                                                     """);

        var graphql = new HotChocolateSemanticAdapter(model).Adapt("""
                                                                   query {
                                                                     customer(where: { name: { eq: "Alice" } }) { name }
                                                                   }
                                                                   """);

        var jsonRequest = new ReadIntentCompiler(model).Compile(json);

        Assert.Equal(jsonRequest.Root, graphql.Root);
        Assert.Equal(new FieldId(101), Assert.Single(jsonRequest.Selections).Field);
        Assert.Equal(new FieldId(101), Assert.Single(graphql.Selections).Field);
        Assert.IsType<SemanticFieldFilter>(jsonRequest.Options!.Filter);
        Assert.IsType<SemanticFieldFilter>(graphql.Options!.Filter);
    }

    private static void AssertSemanticRequestsEqual(SemanticRequest expected, SemanticRequest actual)
    {
        Assert.Equal(expected.Root, actual.Root);
        Assert.Equal(expected.Selections.Count, actual.Selections.Count);
        for (var i = 0; i < expected.Selections.Count; i++)
            AssertSelectionEqual(expected.Selections[i], actual.Selections[i]);

        var expectedOptions = expected.Options!;
        var actualOptions = actual.Options!;
        Assert.Equal(expectedOptions.Limit, actualOptions.Limit);
        Assert.Equal(expectedOptions.Offset, actualOptions.Offset);
        Assert.Equal(expectedOptions.After, actualOptions.After);
        AssertFilterEqual(expectedOptions.Filter, actualOptions.Filter);
        Assert.Equal(expectedOptions.EffectiveOrder.Count, actualOptions.EffectiveOrder.Count);
        for (var i = 0; i < expectedOptions.EffectiveOrder.Count; i++)
        {
            var left = expectedOptions.EffectiveOrder[i];
            var right = actualOptions.EffectiveOrder[i];
            Assert.Equal(left.Field, right.Field);
            Assert.Equal(left.Direction, right.Direction);
            Assert.Equal(left.Aggregate, right.Aggregate);
            Assert.Equal(left.EffectivePath, right.EffectivePath);
        }
    }

    private static void AssertSelectionEqual(SemanticSelection expected, SemanticSelection actual)
    {
        Assert.Equal(expected.Field, actual.Field);
        Assert.Equal(expected.Relationship, actual.Relationship);
        Assert.Equal(expected.Children.Count, actual.Children.Count);
        for (var i = 0; i < expected.Children.Count; i++)
            AssertSelectionEqual(expected.Children[i], actual.Children[i]);
    }

    private static void AssertFilterEqual(SemanticFilterExpression? expected, SemanticFilterExpression? actual)
    {
        if (expected is null || actual is null)
        {
            Assert.Equal(expected is null, actual is null);
            return;
        }

        var left = Assert.IsType<SemanticFieldFilter>(expected);
        var right = Assert.IsType<SemanticFieldFilter>(actual);
        Assert.Equal(left.Field, right.Field);
        Assert.Equal(left.Operator, right.Operator);
        Assert.Equal(left.Value?.ToString(), right.Value?.ToString());
    }
}