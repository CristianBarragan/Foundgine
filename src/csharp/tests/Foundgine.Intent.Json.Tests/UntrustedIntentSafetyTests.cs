using Xunit;

namespace Foundgine.Core.Serialization.Tests;

public sealed class UntrustedIntentSafetyTests
{
    [Fact]
    public void Rejects_selection_depth_before_recursive_conversion_can_grow_unbounded()
    {
        const string json = """
                            {
                              "rootEntity": "Customer",
                              "selections": [
                                { "relationship": "Accounts", "children": [
                                  { "relationship": "Customer", "children": [
                                    { "relationship": "Accounts", "children": [
                                      { "field": "Id" }
                                    ] }
                                  ] }
                                ] }
                              ]
                            }
                            """;

        var adapter = new JsonReadIntentAdapter(new JsonReadIntentAdapterOptions
        {
            MaxSelectionDepth = 2
        });

        var exception = Assert.Throws<InvalidOperationException>(() => adapter.Parse(json));
        Assert.Contains("Selection depth", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_excessive_filter_nodes()
    {
        const string json = """
                            {
                              "rootEntity": "Customer",
                              "selections": [{ "field": "Name" }],
                              "filter": {
                                "kind": "and",
                                "expressions": [
                                  { "kind": "field", "field": "Name", "operator": "Eq", "value": "Alice" },
                                  { "kind": "field", "field": "Name", "operator": "Eq", "value": "Bob" },
                                  { "kind": "field", "field": "Name", "operator": "Eq", "value": "Eve" }
                                ]
                              }
                            }
                            """;

        var adapter = new JsonReadIntentAdapter(new JsonReadIntentAdapterOptions
        {
            MaxFilterNodes = 2
        });

        var exception = Assert.Throws<InvalidOperationException>(() => adapter.Parse(json));
        Assert.Contains("Filter node count", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_excessive_selection_count()
    {
        const string json = """
                            {
                              "rootEntity": "Customer",
                              "selections": [
                                { "field": "Id" },
                                { "field": "Name" },
                                { "field": "Id" }
                              ]
                            }
                            """;

        var adapter = new JsonReadIntentAdapter(new JsonReadIntentAdapterOptions
        {
            MaxSelections = 2
        });

        var exception = Assert.Throws<InvalidOperationException>(() => adapter.Parse(json));
        Assert.Contains("Selection count", exception.Message, StringComparison.Ordinal);
    }
}