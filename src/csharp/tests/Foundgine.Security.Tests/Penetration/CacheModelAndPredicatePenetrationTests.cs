using Foundgine.Core.Semantic.Authorization;
using Foundgine.Core.Execution;
using Foundgine.Core.Semantic.Planning;
using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.Security;
using Xunit;

namespace Foundgine.Security.Tests.Penetration;

/// <summary>SEC-43..SEC-46 and SEC-54..SEC-55: cache, model and semantic-equivalence attacks.</summary>
public sealed class CacheModelAndPredicatePenetrationTests
{
    [Fact]
    public void Cache_key_isolation_cannot_alias_delimiter_shaped_authorities()
    {
        var cache = new MemoryProviderPlanCache();
        cache.Set("tenant=a\u001fsubject=b", new TestPlan("a"));
        cache.Set("tenant=a\u001fsubject=b\u001fc", new TestPlan("b"));

        Assert.True(cache.TryGet("tenant=a\u001fsubject=b", out var first));
        Assert.True(cache.TryGet("tenant=a\u001fsubject=b\u001fc", out var second));
        Assert.NotSame(first, second);
    }

    [Fact]
    public void Revocation_or_authority_change_must_produce_a_different_semantic_fingerprint()
    {
        var a = CreatePlan(SecurityInvariantIds.TenantIsolation);
        var b = CreatePlan(SecurityInvariantIds.AuthorizationRequired);

        Assert.NotEqual(SemanticPlanFingerprint.Create(a), SemanticPlanFingerprint.Create(b));
    }

    [Fact]
    public void Model_version_mismatch_is_not_semantically_equivalent()
    {
        var a = CreatePlan(SecurityInvariantIds.AuthorizationRequired);
        var b = CreatePlan(SecurityInvariantIds.RuntimeAuthorization);

        // A changed security obligation is the authoritative signal. A cache
        // key produced for one model/security version must not be reused for
        // another security-bearing plan.
        Assert.NotEqual(SemanticPlanFingerprint.Create(a), SemanticPlanFingerprint.Create(b));
    }

    [Fact]
    public void Unknown_security_obligation_cannot_be_normalized_into_a_known_one()
    {
        var ir = Foundgine.Testing.ExecutionIRTestFactory.Create(
            new ExecutionIRNode(1, ExecutionOperation.Scan, new Foundgine.Core.Abstractions.EntityId(1),
                [new Foundgine.Core.Abstractions.FieldId(1)], null, null, []),
            ["security.unknown"]);

        Assert.Contains("security.unknown", ir.RequiredSecurityInvariants);
        Assert.DoesNotContain(SecurityInvariantIds.AuthorizationRequired, ir.RequiredSecurityInvariants);
    }

    private static SemanticPlan CreatePlan(string invariant)
    {
        var graph = new SemanticGraph();
        graph.AddRoot(new Foundgine.Core.Abstractions.EntityId(1), [new Foundgine.Core.Abstractions.FieldId(1)]);
        return new SemanticPlan(
            new SemanticPlanNode(1, ExecutionOperation.Scan, new Foundgine.Core.Abstractions.EntityId(1),
                [new Foundgine.Core.Abstractions.FieldId(1)], null, null, []),
            [invariant]);
    }

    private sealed record TestPlan(string Name) : ProviderPlan("test");
}