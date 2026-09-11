using Foundgine.Core.Serialization;
using Xunit;

namespace Foundgine.Security.Tests.Penetration;

/// <summary>SEC-45 and SEC-62: hostile graph, JSON and resource-boundary inputs.</summary>
public sealed class GraphAndResourceDoSPenetrationTests
{
    [Fact]
    public void Deep_relationship_selection_is_rejected_before_semantic_execution()
    {
        var depth = 80;
        var json = "{\"rootEntity\":\"Customer\",\"selections\":[" + BuildNestedSelection(depth) + "]}";
        var adapter = new JsonReadIntentAdapter(new JsonReadIntentAdapterOptions { MaxSelectionDepth = 32 });

        Assert.Throws<InvalidOperationException>(() => adapter.Parse(json));
    }

    [Fact]
    public void Deep_filter_expression_is_rejected_before_planning()
    {
        var json = "{\"rootEntity\":\"Customer\",\"selections\":[{\"field\":\"Id\"}],\"filter\":" +
                   BuildNestedFilter(80) + "}";
        var adapter = new JsonReadIntentAdapter(new JsonReadIntentAdapterOptions { MaxFilterDepth = 32 });

        Assert.Throws<InvalidOperationException>(() => adapter.Parse(json));
    }

    private static string BuildNestedSelection(int depth)
    {
        var value = "{\"field\":\"Id\"}";
        for (var i = 1; i < depth; i++)
            value = $"{{\"relationship\":\"Children\",\"children\":[{value}]}}";
        return value;
    }

    private static string BuildNestedFilter(int depth)
    {
        var value = "{\"kind\":\"field\",\"field\":\"Id\",\"operator\":\"Eq\",\"value\":1}";
        for (var i = 1; i < depth; i++)
            value = $"{{\"kind\":\"and\",\"expressions\":[{value}]}}";
        return value;
    }
}