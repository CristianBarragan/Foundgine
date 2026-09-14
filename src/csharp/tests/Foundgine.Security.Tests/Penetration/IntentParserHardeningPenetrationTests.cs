using Foundgine.Core.Serialization;
using Xunit;

namespace Foundgine.Security.Tests.Penetration;

/// <summary>
/// Additional parser-boundary penetration cases that exercise fail-closed
/// handling for malformed semantic input before authorization or planning.
/// </summary>
public sealed class IntentParserHardeningPenetrationTests
{
    [Fact]
    public void Selection_cannot_claim_field_and_relationship_at_once()
    {
        var adapter = new JsonReadIntentAdapter();

        Assert.Throws<InvalidOperationException>(() => adapter.Parse("""
                                                                     {
                                                                       "rootEntity":"Customer",
                                                                       "selections":[{"field":"Id","relationship":"Orders"}]
                                                                     }
                                                                     """));
    }

    [Fact]
    public void Selection_cannot_be_empty()
    {
        var adapter = new JsonReadIntentAdapter();

        Assert.Throws<InvalidOperationException>(() => adapter.Parse("""
                                                                     {
                                                                       "rootEntity":"Customer",
                                                                       "selections":[{}]
                                                                     }
                                                                     """));
    }

    [Fact]
    public void Negative_limit_and_offset_are_rejected()
    {
        var adapter = new JsonReadIntentAdapter();

        Assert.Throws<InvalidOperationException>(() => adapter.Parse("""
                                                                     {
                                                                       "rootEntity":"Customer",
                                                                       "selections":[{"field":"Id"}],
                                                                       "limit":-1
                                                                     }
                                                                     """));

        Assert.Throws<InvalidOperationException>(() => adapter.Parse("""
                                                                     {
                                                                       "rootEntity":"Customer",
                                                                       "selections":[{"field":"Id"}],
                                                                       "offset":-1
                                                                     }
                                                                     """));
    }

    [Fact]
    public void Unsupported_filter_kind_cannot_fall_through_to_provider_semantics()
    {
        var adapter = new JsonReadIntentAdapter();

        Assert.Throws<InvalidOperationException>(() => adapter.Parse("""
                                                                     {
                                                                       "rootEntity":"Customer",
                                                                       "selections":[{"field":"Id"}],
                                                                       "filter":{"kind":"rawSql","value":"1=1"}
                                                                     }
                                                                     """));
    }

    [Fact]
    public void Relationship_filter_without_predicate_is_rejected()
    {
        var adapter = new JsonReadIntentAdapter();

        Assert.Throws<InvalidOperationException>(() => adapter.Parse("""
                                                                     {
                                                                       "rootEntity":"Customer",
                                                                       "selections":[{"field":"Id"}],
                                                                       "filter":{
                                                                         "kind":"relationship",
                                                                         "relationship":"Orders",
                                                                         "quantifier":"Any"
                                                                       }
                                                                     }
                                                                     """));
    }

    [Fact]
    public void Deep_json_value_is_rejected_before_materialization_completes()
    {
        const int depth = 40;
        var value = "1";

        for (var i = 0; i < depth; i++)
            value = $"{{\"nested\":{value}}}";

        var adapter = new JsonReadIntentAdapter(
            new JsonReadIntentAdapterOptions { MaxJsonValueDepth = 16 });

        var json = $$"""
                     {
                       "rootEntity":"Customer",
                       "selections":[{"field":"Id"}],
                       "filter":{
                         "kind":"field",
                         "field":"Metadata",
                         "operator":"Eq",
                         "value":{{value}}
                       }
                     }
                     """;

        Assert.Throws<InvalidOperationException>(() => adapter.Parse(json));
    }

    [Fact]
    public void Empty_filter_expression_list_is_rejected()
    {
        var adapter = new JsonReadIntentAdapter();

        Assert.Throws<InvalidOperationException>(() => adapter.Parse("""
                                                                     {
                                                                       "rootEntity":"Customer",
                                                                       "selections":[{"field":"Id"}],
                                                                       "filter":{"kind":"or","expressions":[]}
                                                                     }
                                                                     """));
    }
}