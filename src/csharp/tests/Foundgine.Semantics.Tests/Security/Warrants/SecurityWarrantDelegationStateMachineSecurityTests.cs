using Foundgine.Core.Semantic.Security.Warrants;
using Xunit;

namespace Foundgine.Core.Semantic.Tests.Security.Warrants;

public sealed class SecurityWarrantDelegationStateMachineSecurityTests
{
    [Fact]
    public void Register_rotate_and_redelegate_form_a_linearizable_lifecycle()
    {
        var warrant = Create();
        var machine = new SecurityWarrantDelegationStateMachine();
        var registered = machine.Register(warrant, "key-v1");

        Assert.Equal(1, registered.Sequence);
        Assert.Equal("key-v1", registered.ActiveKeyId);

        var rotated = machine.RotateKey(warrant, "key-v2");
        Assert.Equal(2, rotated.After.Sequence);
        Assert.Equal("key-v2", rotated.After.ActiveKeyId);
        Assert.Equal(SecurityWarrantDelegationState.Active, rotated.After.State);

        var delegation = machine.AssertCanDelegate(warrant);
        Assert.Equal(2, delegation.Sequence);
    }

    [Fact]
    public void Revocation_and_compromise_are_terminal_for_delegation()
    {
        var warrant = Create();
        var machine = new SecurityWarrantDelegationStateMachine();
        machine.Register(warrant);
        machine.Revoke(warrant);

        Assert.Throws<InvalidOperationException>(() => machine.AssertCanDelegate(warrant));
        Assert.Throws<InvalidOperationException>(() => machine.RotateKey(warrant, "key-v2"));
        Assert.Throws<InvalidOperationException>(() => machine.Revoke(warrant));
    }

    [Fact]
    public void Compromise_cannot_be_cleared_by_key_rotation()
    {
        var warrant = Create();
        var machine = new SecurityWarrantDelegationStateMachine();
        machine.Register(warrant, "key-v1");
        machine.Compromise(warrant);

        Assert.Throws<InvalidOperationException>(() => machine.RotateKey(warrant, "key-v2"));
        Assert.Throws<InvalidOperationException>(() => machine.AssertCanDelegate(warrant));
    }

    [Fact]
    public void Stale_read_cannot_authorize_after_a_state_transition()
    {
        var warrant = Create();
        var machine = new SecurityWarrantDelegationStateMachine();
        machine.Register(warrant);
        var before = machine.Read(warrant);
        machine.Revoke(warrant);
        var after = machine.Read(warrant);

        Assert.Equal(SecurityWarrantDelegationState.Active, before.State);
        Assert.Equal(SecurityWarrantDelegationState.Revoked, after.State);
        Assert.True(after.Sequence > before.Sequence);
    }

    [Fact]
    public async Task Concurrent_revocation_and_key_rotation_have_one_linearizable_winner()
    {
        var warrant = Create();
        var machine = new SecurityWarrantDelegationStateMachine();
        machine.Register(warrant, "key-v1");
        using var gate = new Barrier(3);
        var errors = new System.Collections.Concurrent.ConcurrentBag<Exception>();

        var revoke = Task.Run(() =>
        {
            gate.SignalAndWait();
            try
            {
                machine.Revoke(warrant);
            }
            catch (Exception e)
            {
                errors.Add(e);
            }
        });
        var rotate = Task.Run(() =>
        {
            gate.SignalAndWait();
            try
            {
                machine.RotateKey(warrant, "key-v2");
            }
            catch (Exception e)
            {
                errors.Add(e);
            }
        });
        gate.SignalAndWait();
        await Task.WhenAll(revoke, rotate);

        var state = machine.Read(warrant);
        Assert.True(state.Sequence is 2 or 3);
        Assert.Equal(SecurityWarrantDelegationState.Revoked, state.State);
        Assert.True(errors.Count is 0 or 1);
        if (errors.Count == 1)
            Assert.Contains("Illegal delegation state transition", errors.Single().Message, StringComparison.Ordinal);
    }

    private static SecurityWarrant Create() => new(
        "root", "root-issuer", "agent-a", "foundgine",
        [new CapabilityGrant("Customer.read", "read", ["customer/*"])],
        new SecurityWarrantConstraints(allowedTenants: ["tenant-1"], resourceScopes: ["customer/*"], maxResults: 100),
        DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1), "nonce-root", "key-v1", null, []);
}