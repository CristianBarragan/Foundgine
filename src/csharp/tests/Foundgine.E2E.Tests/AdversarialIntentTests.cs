using Foundgine.E2E.Tests.Banking;
using Foundgine.Core.Serialization;
using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.Intent;
using Foundgine.Core.Semantic.Resolution;
using Foundgine.Providers.Storage.Sql;
using Xunit;
using BankingModel = Foundgine.E2E.Tests.Banking.BankingSemanticModel;

namespace Foundgine.E2E.Tests;

/// <summary>
/// Regression tests for the hostile-agent boundary. The input is treated as
/// untrusted data: semantic names are resolved against the model, values are
/// parameterized by providers, and parser limits constrain recursive input.
/// </summary>
public sealed class AdversarialIntentTests
{
    [Fact]
    public void Unknown_relationship_is_rejected_before_planning()
    {
        const string json = """
                            {
                              "rootEntity": "Customer",
                              "selections": [
                                { "relationship": "AccountsThatDoNotExist", "children": [{ "field": "Id" }] }
                              ]
                            }
                            """;

        var model = BankingModel.Build();
        var intent = new JsonReadIntentAdapter().Parse(json);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new ReadIntentCompiler(model).Compile(intent));

        Assert.Contains("Unknown relationship", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Provider_like_identifiers_are_data_not_sql()
    {
        const string json = """
                            {
                              "rootEntity": "Customer",
                              "selections": [{ "field": "Id" }],
                              "filter": {
                                "kind": "field",
                                "field": "Name",
                                "operator": "Eq",
                                "value": "Alice' OR 1=1 --"
                              }
                            }
                            """;

        var model = BankingModel.Build();
        var intent = new JsonReadIntentAdapter().Parse(json);
        var request = new ReadIntentCompiler(model).Compile(intent);
        var resolved = new SemanticRequestResolver(model.Freeze().CreateSnapshot()).Resolve(request);
        var plan = new Foundgine.Core.Semantic.Planning.Planner().Plan(resolved) with
        {
            AuthorizationBinding =
            new Foundgine.Core.Semantic.Planning.SemanticPlanAuthorizationBinding("test-contract", "test-authorization")
        };
        var sql = new SqlCompiler(BankingRelationalMetadata.Build()).Compile(plan);

        Assert.DoesNotContain("Alice' OR 1=1 --", sql.CommandText, StringComparison.Ordinal);
        Assert.Contains("@p", sql.CommandText, StringComparison.Ordinal);
        Assert.Contains(sql.EffectiveParameters, p => Equals(p.Value, "Alice' OR 1=1 --"));
    }

    [Fact]
    public void Field_selection_cannot_be_turned_into_a_traversal_by_children()
    {
        const string json = """
                            {
                              "rootEntity": "Customer",
                              "selections": [
                                { "field": "Name", "children": [{ "field": "Id" }] }
                              ]
                            }
                            """;

        var model = BankingModel.Build();
        var intent = new JsonReadIntentAdapter().Parse(json);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new ReadIntentCompiler(model).Compile(intent));

        Assert.Contains("cannot have children", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Deep_untrusted_input_is_rejected_by_parser_limits()
    {
        var json = """
                   {
                     "rootEntity": "Customer",
                     "selections": [
                       {
                         "relationship": "Accounts",
                         "children": [
                           {
                             "relationship": "Transactions",
                             "children": [{ "field": "Id" }]
                           }
                         ]
                       }
                     ]
                   }
                   """;

        var limits = new JsonReadIntentAdapterOptions { MaxSelectionDepth = 2 };
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new JsonReadIntentAdapter(limits).Parse(json));

        Assert.Contains("Selection depth exceeds", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Unsupported_filter_kind_never_reaches_semantic_model()
    {
        const string json = """
                            {
                              "rootEntity": "Customer",
                              "selections": [{ "field": "Id" }],
                              "filter": {
                                "kind": "rawSql",
                                "value": "WHERE 1=1"
                              }
                            }
                            """;

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new JsonReadIntentAdapter().Parse(json));

        Assert.Contains("Unsupported filter kind", exception.Message, StringComparison.Ordinal);
    }
}