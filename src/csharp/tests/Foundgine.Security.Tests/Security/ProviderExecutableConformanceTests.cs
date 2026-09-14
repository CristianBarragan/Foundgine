using Foundgine.Core.Execution;
using Foundgine.Core.Execution.Security;
using Foundgine.Core.Semantic.Security;
using Xunit;

namespace Foundgine.Security.Tests.Security;

public sealed class ProviderExecutableConformanceTests
{
    [Fact]
    public void Certification_result_requires_every_required_invariant()
    {
        var result = new ProviderSecurityConformanceResult(
            "test",
            [SecurityInvariantIds.AuthorizationRequired, SecurityInvariantIds.TenantIsolation],
            [SecurityInvariantIds.AuthorizationRequired],
            []);

        Assert.False(result.IsSatisfied);
        Assert.Throws<InvalidOperationException>(() => result.EnsureSatisfied());
    }

    [Fact]
    public void Certification_result_fails_on_provider_violation()
    {
        var result = new ProviderSecurityConformanceResult(
            "test",
            [SecurityInvariantIds.AuthorizationRequired],
            [SecurityInvariantIds.AuthorizationRequired],
            ["authorization predicate was lost"]);

        Assert.False(result.IsSatisfied);
    }
}
