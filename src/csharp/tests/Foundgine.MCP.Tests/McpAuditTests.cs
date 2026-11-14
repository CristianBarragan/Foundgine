using System.Security.Cryptography;
using Foundgine.Providers.Tools.MCP;
using Xunit;

namespace Foundgine.Providers.Tools.MCP.Tests;

public sealed class McpAuditTests
{
    private long _now = 5_000_000;
    private readonly List<FoundgineMcpAuditEvent> _events = new();

    private sealed class ListSink(List<FoundgineMcpAuditEvent> events) : IFoundgineMcpAuditSink
    {
        public void Record(FoundgineMcpAuditEvent e) => events.Add(e);
    }

    private sealed class BrokenSink : IFoundgineMcpAuditSink
    {
        public void Record(FoundgineMcpAuditEvent e) => throw new InvalidOperationException("sink down");
    }

    private List<FoundgineMcpAuditEventType> Types() => _events.Select(e => e.Type).ToList();

    [Fact]
    public void KeyLifecycleIsAuditedWithoutKeyMaterial()
    {
        var ring = new FoundgineMcpApprovalKeyRing(TimeSpan.FromMinutes(5), () => _now, new ListSink(_events));
        using var a = RSA.Create(2048); using var b = RSA.Create(2048); using var c = RSA.Create(2048);
        ring.ActivateSigningKey("k1", a);
        ring.ActivateSigningKey("k2", b, TimeSpan.FromMinutes(10));
        ring.AddVerificationKey("ext", c, DateTimeOffset.FromUnixTimeMilliseconds(_now + (long)TimeSpan.FromMinutes(20).TotalMilliseconds));
        Assert.True(ring.RetireAfter("ext", TimeSpan.FromMinutes(6)));
        Assert.True(ring.Revoke("k1"));
        Assert.Equal(new[]
        {
            FoundgineMcpAuditEventType.KeyActivated, FoundgineMcpAuditEventType.KeyRotated, FoundgineMcpAuditEventType.KeyAdded,
            FoundgineMcpAuditEventType.KeyRetired, FoundgineMcpAuditEventType.KeyRevoked
        }, Types());
        Assert.Equal("k2", _events[1].Subject);
        Assert.Contains("previous=k1", _events[1].Detail);
        Assert.Equal(_now, _events[0].AtUnixMilliseconds);
        Assert.Contains("removed=true", _events[4].Detail);
        foreach (var e in _events) Assert.DoesNotContain("MII", e.ToString());
    }

    [Fact]
    public void FailedOperationsEmitNothingAndABrokenSinkNeverBreaksThem()
    {
        var ring = new FoundgineMcpApprovalKeyRing(TimeSpan.FromMinutes(5), () => _now, new ListSink(_events));
        using var a = RSA.Create(2048);
        ring.ActivateSigningKey("k1", a);
        _events.Clear();
        Assert.Throws<ArgumentException>(() => ring.ActivateSigningKey("k1", a, TimeSpan.FromMinutes(10)));
        Assert.ThrowsAny<ArgumentException>(() => ring.AddVerificationKey("x", a, DateTimeOffset.FromUnixTimeMilliseconds(_now + 1000)));   // too-short retention is an ArgumentOutOfRangeException
        Assert.Empty(_events);

        var broken = new FoundgineMcpApprovalKeyRing(TimeSpan.Zero, () => _now, new BrokenSink());
        broken.ActivateSigningKey("k1", a);
        Assert.True(broken.Revoke("k1"));
        Assert.Throws<InvalidOperationException>(() => broken.KeyId);   // the revocation took effect despite the sink failing
    }

    [Fact]
    public void SessionEventsCarryFingerprintsNotIdsAndNothingForGarbage()
    {
        var manager = new FoundgineMcpSessionManager(
            new FoundgineMcpSessionOptions(TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(25), 1),
            new InMemoryFoundgineMcpSessionStore(), () => _now);
        var gate = new FoundgineMcpSessionGate(manager, new ListSink(_events), () => _now);

        var id = gate.AdmitPost("initialize", null, "alice").IssuedSessionId!;
        Assert.Equal(FoundgineMcpAuditEventType.SessionIssued, _events[0].Type);
        Assert.Equal(FoundgineMcpSessionManager.Fingerprint(id), _events[0].Subject);
        Assert.Equal("alice", _events[0].Actor);

        Assert.Equal(503, gate.AdmitPost("initialize", null, "alice").HttpStatus);
        gate.AdmitPost("ping", null, "alice");
        gate.AdmitPost("ping", id, "mallory");
        gate.AdmitPost("ping", "<script>alert(1)</script>", "alice");
        gate.AdmitDelete(id, "mallory");
        gate.AdmitDelete(id, "alice");
        gate.AdmitDelete(id, "alice");
        Assert.Equal(new[]
        {
            FoundgineMcpAuditEventType.SessionIssued, FoundgineMcpAuditEventType.SessionTableFull,
            FoundgineMcpAuditEventType.SessionRejected, FoundgineMcpAuditEventType.SessionRejected,
            FoundgineMcpAuditEventType.SessionRejected, FoundgineMcpAuditEventType.SessionRejected,
            FoundgineMcpAuditEventType.SessionTerminated, FoundgineMcpAuditEventType.SessionRejected
        }, Types());
        Assert.Contains("MISSING", _events[2].Detail);
        Assert.Contains("OWNER_MISMATCH", _events[3].Detail);
        Assert.Null(_events[4].Subject);   // malformed id: attacker text is never logged
        foreach (var e in _events) Assert.DoesNotContain(id, e.ToString());
        Assert.Equal(12, _events[0].Subject!.Length);
    }
}
