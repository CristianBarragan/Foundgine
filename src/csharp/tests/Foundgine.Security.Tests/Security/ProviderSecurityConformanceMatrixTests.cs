using Foundgine.Core.Execution.Security;
using Foundgine.Core.Semantic.Security;
using Xunit;

namespace Foundgine.Security.Tests.Security;

public sealed class ProviderSecurityConformanceMatrixTests
{
    private static readonly string[] QueryRequirements =
    [
        SecurityInvariantIds.AuthorizationRequired,
        SecurityInvariantIds.RuntimeAuthorization,
        SecurityInvariantIds.FieldVisibility,
        SecurityInvariantIds.ParameterizedValues,
        SecurityInvariantIds.PlanCacheContextIsolation
    ];

    private static readonly string[] TransferRequirements =
    [
        SecurityInvariantIds.TenantIsolation,
        SecurityInvariantIds.RuntimeAuthorization,
        SecurityInvariantIds.AtomicMutation,
        SecurityInvariantIds.Idempotency,
        SecurityInvariantIds.ReplayProtection,
        SecurityInvariantIds.AuditRequired,
        SecurityInvariantIds.ExecutionEvidenceRequired
    ];

    [Fact]
    public void Query_contract_is_supported_by_inmemory_and_sql_profiles()
    {
        var matrix = new ProviderSecurityConformanceMatrix()
            .Register(FoundgineProviderSecurityProfiles.InMemory)
            .Register(FoundgineProviderSecurityProfiles.Sql);

        Assert.True(matrix.Evaluate("in-memory", QueryRequirements).IsSatisfied);
        Assert.True(matrix.Evaluate("sql", QueryRequirements).IsSatisfied);
    }

    [Fact]
    public void Transfer_contract_is_supported_by_high_assurance_postgres_profile()
    {
        var matrix = new ProviderSecurityConformanceMatrix()
            .Register(FoundgineProviderSecurityProfiles.PostgresTransferFunds);

        var proof = matrix.Evaluate("postgres-transfer-funds", TransferRequirements);

        Assert.True(proof.IsSatisfied);
        Assert.Empty(proof.Missing);
        Assert.Contains(SecurityInvariantIds.AtomicMutation, proof.Preserved);
        Assert.Contains(SecurityInvariantIds.Idempotency, proof.Preserved);
        Assert.Contains(SecurityInvariantIds.ReplayProtection, proof.Preserved);
    }

    [Fact]
    public void Generic_sql_cannot_claim_high_assurance_mutation_guarantees()
    {
        var matrix = new ProviderSecurityConformanceMatrix()
            .Register(FoundgineProviderSecurityProfiles.Sql);

        var proof = matrix.Evaluate("sql", TransferRequirements);

        Assert.False(proof.IsSatisfied);
        Assert.Contains(SecurityInvariantIds.AtomicMutation, proof.Missing);
        Assert.Contains(SecurityInvariantIds.Idempotency, proof.Missing);
        Assert.Contains(SecurityInvariantIds.AuditRequired, proof.Missing);
    }

    [Fact]
    public void Unknown_provider_fails_closed()
    {
        var matrix = new ProviderSecurityConformanceMatrix()
            .Register(FoundgineProviderSecurityProfiles.Sql);

        Assert.Throws<KeyNotFoundException>(() => matrix.Evaluate("unknown", QueryRequirements));
    }

    [Fact]
    public void Provider_cannot_register_unknown_invariant()
    {
        var profile = new ProviderSecurityConformanceProfile(
            "hostile-provider",
            ["security.fake"],
            []);

        Assert.Throws<InvalidOperationException>(() =>
            new ProviderSecurityConformanceMatrix().Register(profile));
    }
}

// Adversarial provider claims: a profile is not an authorization grant and an
// incomplete/unknown claim must fail closed.
public sealed class ProviderSecurityAttackTests
{
    [Fact]
    public void Provider_lying_about_preservation_cannot_satisfy_an_unclaimed_requirement()
    {
        var matrix = new ProviderSecurityConformanceMatrix()
            .Register(new ProviderSecurityConformanceProfile(
                "hostile-provider",
                [SecurityInvariantIds.AuthorizationRequired],
                ["hostile provider claims it preserves authorization only"]));

        var proof = matrix.Evaluate("hostile-provider", [
            SecurityInvariantIds.AuthorizationRequired,
            SecurityInvariantIds.TenantIsolation
        ]);

        Assert.False(proof.IsSatisfied);
        Assert.Contains(SecurityInvariantIds.TenantIsolation, proof.Missing);
    }

    [Fact]
    public void Provider_claims_are_never_allowed_to_invent_unknown_invariants()
    {
        var profile = new ProviderSecurityConformanceProfile(
            "hostile-provider",
            [SecurityInvariantIds.AuthorizationRequired],
            []);

        Assert.Throws<InvalidOperationException>(() =>
            new ProviderSecurityConformanceMatrix()
                .Register(profile with { PreservedSecurityInvariants = ["security.provider-lied"] }));
    }
}