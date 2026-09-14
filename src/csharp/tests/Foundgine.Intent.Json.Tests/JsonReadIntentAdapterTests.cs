using Foundgine.Core.Serialization;
using Foundgine.Core.Semantic.Intent;
using Foundgine.Core.Semantic.Query;
using Xunit;

namespace Foundgine.Core.Serialization.Tests;

public sealed class JsonReadIntentAdapterTests
{
    [Fact]
    public void Parses_nested_relationship_filter_and_order()
    {
        const string json = """
                            {
                              "rootEntity": "Transaction",
                              "selections": [
                                { "field": "Id" },
                                { "field": "Amount" },
                                { "field": "TransactionDate" }
                              ],
                              "filter": {
                                "kind": "relationship",
                                "relationship": "Account",
                                "quantifier": "Some",
                                "predicate": {
                                  "kind": "relationship",
                                  "relationship": "Customer",
                                  "quantifier": "Some",
                                  "predicate": {
                                    "kind": "field",
                                    "field": "Name",
                                    "operator": "Eq",
                                    "value": "Alice"
                                  }
                                }
                              },
                              "order": [
                                { "field": "TransactionDate", "direction": "Desc" }
                              ],
                              "limit": 5
                            }
                            """;

        var intent = new JsonReadIntentAdapter().Parse(json);

        Assert.Equal("Transaction", intent.RootEntity);
        Assert.Equal(3, intent.Selections.Count);
        var accountFilter = Assert.IsType<ReadRelationshipFilter>(intent.Filter);
        var customerFilter = Assert.IsType<ReadRelationshipFilter>(accountFilter.Predicate);
        var nameFilter = Assert.IsType<ReadFieldFilter>(customerFilter.Predicate);
        Assert.Equal("Alice", nameFilter.Value);
        Assert.Equal(SemanticSortDirection.Desc, Assert.Single(intent.Order!).Direction);
        Assert.Equal(5, intent.Limit);
    }

    [Fact]
    public void Parses_json_arrays_and_objects_as_provider_neutral_values()
    {
        const string json = """
                            {
                              "rootEntity": "Customer",
                              "selections": [{ "field": "Name" }],
                              "filter": {
                                "kind": "field",
                                "field": "Name",
                                "operator": "In",
                                "value": ["Alice", "Bob"]
                              }
                            }
                            """;

        var filter = Assert.IsType<ReadFieldFilter>(new JsonReadIntentAdapter().Parse(json).Filter);
        var values = Assert.IsType<object[]>(filter.Value);
        Assert.Equal(new object[] { "Alice", "Bob" }, values);
    }

    [Fact]
    public void Rejects_unsupported_filter_kind()
    {
        const string json = """
                            {
                              "rootEntity": "Customer",
                              "selections": [{ "field": "Name" }],
                              "filter": { "kind": "sql", "field": "Name", "value": "Alice" }
                            }
                            """;

        var exception = Assert.Throws<InvalidOperationException>(() => new JsonReadIntentAdapter().Parse(json));
        Assert.Contains("Unsupported filter kind", exception.Message);
    }
}