using Foundgine.HighAssurance.Banking;
using Foundgine.HighAssurance.Postgres.Execution;
using Xunit;

namespace Foundgine.Runtime.ControlPlane.Tests;

/// <summary>adversarial tests for decision-to-execution binding and TOCTOU resistance.</summary>
public sealed class AuthorizationExecutionBindingSecurityTests
{
    private static readonly Guid Actor = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Source = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid Destination = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private static TransferFundsCommand Command(decimal amount = 10m, string key = "k-1") =>
        new(Source, Destination, amount, key);

    private static AuthorizationDecision Evidence(long version = 7) =>
        new(true, version, "fp-7");

    [Fact]
    public void Exact_request_and_evidence_are_bound()
    {
        var command = Command();
        var binding = AuthorizationExecutionBinding.Create(Actor, 7, command, Evidence());
        binding.ValidateAgainst(Actor, 7, command, Evidence());
    }

    [Fact]
    public void Different_amount_cannot_reuse_authorization_evidence()
    {
        var binding = AuthorizationExecutionBinding.Create(Actor, 7, Command(10m), Evidence());
        Assert.Throws<InvalidOperationException>(() =>
            binding.ValidateAgainst(Actor, 7, Command(11m), Evidence()));
    }

    [Fact]
    public void Different_idempotency_key_cannot_reuse_authorization_evidence()
    {
        var binding = AuthorizationExecutionBinding.Create(Actor, 7, Command(key: "k-1"), Evidence());
        Assert.Throws<InvalidOperationException>(() =>
            binding.ValidateAgainst(Actor, 7, Command(key: "k-2"), Evidence()));
    }

    [Fact]
    public void Different_resource_cannot_reuse_authorization_evidence()
    {
        var binding = AuthorizationExecutionBinding.Create(Actor, 7, Command(), Evidence());
        var different = new TransferFundsCommand(
            Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"), Destination, 10m, "k-1");
        Assert.Throws<InvalidOperationException>(() =>
            binding.ValidateAgainst(Actor, 7, different, Evidence()));
    }

    [Fact]
    public void Different_actor_or_tenant_cannot_reuse_binding()
    {
        var command = Command();
        var binding = AuthorizationExecutionBinding.Create(Actor, 7, command, Evidence());
        Assert.Throws<InvalidOperationException>(() => binding.ValidateAgainst(
            Guid.Parse("22222222-2222-2222-2222-222222222222"), 7, command, Evidence()));
        Assert.Throws<InvalidOperationException>(() => binding.ValidateAgainst(Actor, 8, command, Evidence()));
    }

    [Fact]
    public void Different_authorization_version_cannot_cross_the_execution_gate()
    {
        var command = Command();
        var binding = AuthorizationExecutionBinding.Create(Actor, 7, command, Evidence(7));
        Assert.Throws<InvalidOperationException>(() =>
            binding.ValidateAgainst(Actor, 7, command, new AuthorizationDecision(true, 8, "fp-8")));
    }

    [Fact]
    public void Different_authorization_fingerprint_cannot_cross_the_execution_gate()
    {
        var command = Command();
        var binding = AuthorizationExecutionBinding.Create(Actor, 7, command, Evidence());
        Assert.Throws<InvalidOperationException>(() =>
            binding.ValidateAgainst(Actor, 7, command, new AuthorizationDecision(true, 7, "forged")));
    }

    [Fact]
    public void Denied_evidence_cannot_be_bound()
    {
        // A denied AuthorizationDecision is rejected by Create() itself -- before
        // ValidateAgainst is ever reached -- with UnauthorizedAccessException,
        // a more precise type than the generic InvalidOperationException the
        // other tests above expect from ValidateAgainst's tamper/mismatch checks.
        Assert.Throws<UnauthorizedAccessException>(() =>
            AuthorizationExecutionBinding.Create(Actor, 7, Command(), new AuthorizationDecision(false, 7, "fp-7"))
                .ValidateAgainst(Actor, 7, Command(), new AuthorizationDecision(false, 7, "fp-7")));
    }
}