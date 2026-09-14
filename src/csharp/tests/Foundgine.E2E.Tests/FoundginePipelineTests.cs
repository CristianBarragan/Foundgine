using Foundgine.E2E.Tests.Banking;
using Foundgine.Core.Semantic.Metadata;
using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Planning;
using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.Authorization;
using Foundgine.Core.Semantic.Resolution;
using Xunit;

namespace Foundgine.E2E.Tests;

public sealed class FoundginePipelineTests
{
    [Fact]
    public void Banking_thesis_reaches_provider_independent_execution_plan()
    {
        var model = BankingSemanticModel.Build();

        var request = new SemanticRequest(
            BankingSemanticModel.Customer,
            [
                new SemanticSelection(new FieldId(1), null, []),
                new SemanticSelection(
                    null,
                    BankingSemanticModel.CustomerAccounts,
                    [
                        new SemanticSelection(new FieldId(1), null, []),
                        new SemanticSelection(
                            null,
                            BankingSemanticModel.AccountTransactions,
                            [
                                new SemanticSelection(new FieldId(1), null, []),
                                new SemanticSelection(new FieldId(3), null, [])
                            ])
                    ])
            ]);

        var resolved = new SemanticRequestResolver(model.Freeze().CreateSnapshot()).Resolve(request);
        var authorized = new SemanticAuthorizer(new AllowAllSemanticAuthorizationPolicy()).Authorize(resolved);
        var plan = new Planner().Plan(authorized);

        Assert.Equal(new EntityId(1), plan.Root.EntityId);
        Assert.Equal(ExecutionOperation.Scan, plan.Root.Operation);
        Assert.Equal(new[] { new FieldId(1) }, plan.Root.Fields);

        var account = Assert.Single(plan.Root.Children);
        Assert.Equal(new EntityId(2), account.EntityId);
        Assert.Equal(ExecutionOperation.Traverse, account.Operation);
        Assert.Equal(new RelationshipId(1), account.ViaRelationship);
        Assert.Equal(new[] { new FieldId(1) }, account.Fields);

        var transaction = Assert.Single(account.Children);
        Assert.Equal(new EntityId(3), transaction.EntityId);
        Assert.Equal(ExecutionOperation.Traverse, transaction.Operation);
        Assert.Equal(new RelationshipId(2), transaction.ViaRelationship);
        Assert.Equal(
            new[] { new FieldId(1), new FieldId(3) },
            transaction.Fields);
    }
}