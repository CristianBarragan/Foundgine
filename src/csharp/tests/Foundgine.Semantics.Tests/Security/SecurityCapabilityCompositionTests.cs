using Foundgine.Core.Semantic.Capabilities;
using Foundgine.Core.Semantic.Security;
using Foundgine.Core.Semantic.Security.Warrants;
using Xunit;

namespace Foundgine.Core.Semantic.Tests.Security;

public sealed class SecurityCapabilityCompositionTests
{
    [Fact]
    public void Composition_requires_every_capability_to_be_independently_authorized()
    {
        var warrant = Warrant([new CapabilityGrant("Customer.read", "read", ["customer/*"])]);
        var customer = Capability("Customer.read", "read");
        var order = Capability("Order.read", "read");

        var result = SecurityCapabilityComposition.Validate(
            [customer, order], warrant, "agent", "foundgine", "tenant-1", "customer/*");

        Assert.False(result.IsSatisfied);
        Assert.Contains("Order.read", result.FailureReason, StringComparison.Ordinal);
    }

    [Fact]
    public void Composition_never_unions_authority_across_tenants()
    {
        var warrant = Warrant([
                new CapabilityGrant("Customer.read", "read", ["customer/*"]),
                new CapabilityGrant("Order.read", "read", ["order/*"])
            ]) with
            {
                Constraints = new SecurityWarrantConstraints(allowedTenants: ["tenant-1"])
            };

        var result = SecurityCapabilityComposition.Validate(
            [Capability("Customer.read", "read"), Capability("Order.read", "read")],
            warrant, "agent", "foundgine", "tenant-2", "customer/*");

        Assert.False(result.IsSatisfied);
    }

    [Fact]
    public void Composition_uses_union_of_required_invariants_but_not_union_of_authority()
    {
        var warrant = Warrant([
            new CapabilityGrant("Customer.read", "read", ["customer/*"]),
            new CapabilityGrant("Order.read", "read", ["order/*"])
        ]);

        var customer = Capability("Customer.read", "read", fields: []) with
        {
            RequiredSecurityInvariants = [SecurityInvariantIds.AuthorizationRequired]
        };
        var order = Capability("Order.read", "read") with
        {
            RequiredSecurityInvariants = [SecurityInvariantIds.FieldVisibility]
        };

        var result = SecurityCapabilityComposition.Validate(
            [customer, order], warrant, "agent", "foundgine", null, "shared/*");

        Assert.True(result.IsSatisfied);
        Assert.Equal(
            [SecurityInvariantIds.AuthorizationRequired, SecurityInvariantIds.FieldVisibility],
            result.EffectiveSecurityInvariants);
    }

    [Fact]
    public void Composition_rejects_field_outside_warrant_scope()
    {
        var warrant = Warrant([new CapabilityGrant("Customer.read", "read", ["customer/*"])]) with
        {
            Constraints = new SecurityWarrantConstraints(allowedFields: ["Id", "Name"])
        };

        var result = SecurityCapabilityComposition.Validate(
            [Capability("Customer.read", "read")], warrant, "agent", "foundgine", null, "customer/*",
            ["Id", "Balance"]);

        Assert.False(result.IsSatisfied);
        Assert.Contains("field", result.FailureReason!, StringComparison.OrdinalIgnoreCase);
    }

    private static SemanticCapability Capability(string id, string operation, IReadOnlyList<string>? fields = null)
    {
        var effectiveFields = fields ?? ["Id", "Name"];
        return new(
            id,
            id,
            new Foundgine.Core.Abstractions.EntityId(1),
            Foundgine.Core.Abstractions.AuthorizationDecision.Allowed,
            [], [], [], effectiveFields, [])
        {
            Operation = operation,
            RequiredSecurityInvariants = effectiveFields.Count > 0
                ? [SecurityInvariantIds.AuthorizationRequired, SecurityInvariantIds.FieldVisibility]
                : [SecurityInvariantIds.AuthorizationRequired]
        };
    }

    private static SecurityWarrant Warrant(IReadOnlyList<CapabilityGrant> grants) => new(
        "warrant", "issuer", "agent", "foundgine", grants,
        SecurityWarrantConstraints.Unrestricted,
        DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1),
        "nonce", "key", null, []);
}