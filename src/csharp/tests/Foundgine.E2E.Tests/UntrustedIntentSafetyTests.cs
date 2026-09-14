using Foundgine.Core.Abstractions;
using Foundgine.E2E.Tests.Banking;
using Foundgine.Core.Serialization;
using Foundgine.Core.Semantic.Planning;
using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.Authorization;
using Foundgine.Core.Semantic.Intent;
using Foundgine.Core.Semantic.Resolution;
using Foundgine.Providers.Storage.Sql;
using Xunit;
using BankingModel = Foundgine.E2E.Tests.Banking.BankingSemanticModel;

namespace Foundgine.E2E.Tests;

public sealed class UntrustedIntentSafetyTests
{
    [Fact]
    public void Unknown_field_is_rejected_before_planning()
    {
        const string json = """
                            {
                              "rootEntity": "Customer",
                              "selections": [{ "field": "DropDatabase" }]
                            }
                            """;

        var model = BankingModel.Build();
        var intent = new JsonReadIntentAdapter().Parse(json);

        var exception = Assert.Throws<InvalidOperationException>(() =>
        {
            new ReadIntentCompiler(model).Compile(intent);
        });

        Assert.Contains("Unknown field", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Authorization_cannot_be_bypassed_by_json_intent()
    {
        const string json = """
                            {
                              "rootEntity": "Account",
                              "selections": [
                                { "field": "Id" },
                                { "field": "Balance" }
                              ]
                            }
                            """;

        var model = BankingModel.Build();
        var intent = new JsonReadIntentAdapter().Parse(json);
        var request = new ReadIntentCompiler(model).Compile(intent);
        var resolved = new SemanticRequestResolver(model.Freeze().CreateSnapshot()).Resolve(request);
        var authorized = new SemanticAuthorizer(new DenyBalancePolicy()).Authorize(resolved);

        Assert.Single(authorized.Nodes);
        Assert.Equal(new[] { new FieldId(1) }, authorized.Nodes[0].Fields);
        Assert.DoesNotContain(new FieldId(3), authorized.Nodes[0].Fields);

        var plan = new Planner().Plan(authorized) with
        {
            AuthorizationBinding = new SemanticPlanAuthorizationBinding("test-contract", "test-authorization")
        };
        var sql = new SqlCompiler(BankingRelationalMetadata.Build()).Compile(plan);

        Assert.DoesNotContain("Balance", sql.CommandText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Denied_root_entity_stops_untrusted_intent_before_planning()
    {
        const string json = """
                            {
                              "rootEntity": "Account",
                              "selections": [{ "field": "Id" }]
                            }
                            """;

        var model = BankingModel.Build();
        var intent = new JsonReadIntentAdapter().Parse(json);
        var request = new ReadIntentCompiler(model).Compile(intent);
        var resolved = new SemanticRequestResolver(model.Freeze().CreateSnapshot()).Resolve(request);

        var exception = Assert.Throws<SemanticAuthorizationException>(() =>
            new SemanticAuthorizer(new DenyAccountPolicy()).Authorize(resolved));

        Assert.Contains("Access denied", exception.Message, StringComparison.Ordinal);
    }

    private sealed class DenyBalancePolicy : AllowAllSemanticAuthorizationPolicy
    {
        public override bool CanAccessField(EntityId entityId, FieldId fieldId) =>
            entityId != BankingModel.Account || fieldId != new FieldId(3);
    }

    private sealed class DenyAccountPolicy : AllowAllSemanticAuthorizationPolicy
    {
        public override bool CanAccessEntity(EntityId entityId) => entityId != BankingModel.Account;
    }
}