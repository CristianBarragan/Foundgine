using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Authorization;
using Xunit;

namespace Foundgine.Core.Semantic.Planning.Tests;

public sealed class AuthorizationCanonicalizationIdempotenceTests
{
    [Fact]
    public void Already_canonical_authorization_does_not_rewrite_forever()
    {
        var entity = new EntityId(1);
        var predicate = AuthorizationPredicate.And(
            AuthorizationPredicate.Member(AuthorizationPredicate.ResourceParameter("resource"), "TenantId"),
            AuthorizationPredicate.Member(AuthorizationPredicate.ResourceParameter("resource"), "RegionId"));
        var plan = new SemanticPlan(
            new SemanticPlanNode(1, ExecutionOperation.Scan, entity, [new FieldId(1)], null, null, [],
                Authorization: predicate),
            ["authorization.required", "authorization.runtime"]);

        var rule = new AuthorizationCanonicalizationRule();
        var first = rule.Apply(plan);
        var second = rule.Apply(first);

        Assert.Equal(SemanticPlanFingerprint.Create(first), SemanticPlanFingerprint.Create(second));
        Assert.Same(first, second);
    }
}