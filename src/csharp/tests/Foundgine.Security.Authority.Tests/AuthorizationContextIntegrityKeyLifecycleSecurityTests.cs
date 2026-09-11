using System.Security.Cryptography;
using System.Text;
using Foundgine.Runtime.ControlPlane;
using Foundgine.HighAssurance.Postgres.Execution;
using Xunit;

namespace Foundgine.Runtime.ControlPlane.Tests;

/// <summary>
/// : adversarial tests for external authorization-integrity key lifecycle.
/// These tests are runtime-independent; no PostgreSQL instance is required.
/// </summary>
public sealed class AuthorizationContextIntegrityKeyLifecycleSecurityTests
{
    [Fact]
    public void Rotation_makes_previous_active_key_verification_only_and_new_key_active()
    {
        var ring = new AuthorizationContextIntegrityKeyRing(Key("key-v1", "old"));

        var rotated = ring.Rotate(Key("key-v2", "new"), Provenance(1));

        Assert.Equal("key-v2", rotated.ActiveKeyId);
        Assert.Equal(AuthorizationIntegrityKeyState.VerificationOnly, rotated.GetState("key-v1"));
        Assert.Equal(AuthorizationIntegrityKeyState.Active, rotated.GetState("key-v2"));
        Assert.True(rotated.CanVerify("key-v1"));
        Assert.Equal(2, rotated.ConfigurationVersion);
    }

    [Fact]
    public void Retired_key_cannot_verify_existing_evidence()
    {
        var actor = Guid.NewGuid();
        var ring = new AuthorizationContextIntegrityKeyRing(Key("key-v1", "old"));
        var tag = ring.ComputeContextTag(actor, 10, true, 1, "fp");

        ring = ring.Rotate(Key("key-v2", "new"), Provenance(1));
        ring = ring.Retire("key-v1", Provenance(2), new HashSet<string>());

        Assert.Equal(AuthorizationIntegrityKeyState.Retired, ring.GetState("key-v1"));
        Assert.False(ring.VerifyContextTag(actor, 10, true, 1, "fp",
            AuthorizationContextIntegrityKeyRing.CurrentAlgorithmVersion, "key-v1", tag));
    }

    [Fact]
    public void Active_key_cannot_be_retired()
    {
        var ring = new AuthorizationContextIntegrityKeyRing(Key("key-v1", "old"));

        var ex = Assert.Throws<InvalidOperationException>(() =>
            ring.Retire("key-v1", Provenance(1), new HashSet<string>()));

        Assert.Contains("active", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Key_still_referenced_by_persisted_evidence_cannot_be_retired()
    {
        var ring = new AuthorizationContextIntegrityKeyRing(Key("key-v1", "old"))
            .Rotate(Key("key-v2", "new"), Provenance(1));

        var ex = Assert.Throws<InvalidOperationException>(() =>
            ring.Retire("key-v1", Provenance(2),
                new HashSet<string>(StringComparer.Ordinal) { "key-v1" }));

        Assert.Contains("still referenced", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rotation_sequence_is_monotonic_and_replay_is_rejected()
    {
        var ring = new AuthorizationContextIntegrityKeyRing(Key("key-v1", "old"));

        ring = ring.Rotate(Key("key-v2", "new"), Provenance(7));

        var replay = Assert.Throws<InvalidOperationException>(() =>
            ring.Rotate(Key("key-v3", "newer"), Provenance(7)));

        Assert.Contains("stale", replay.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Retired_key_cannot_be_reactivated()
    {
        var ring = new AuthorizationContextIntegrityKeyRing(Key("key-v1", "old"))
            .Rotate(Key("key-v2", "new"), Provenance(1))
            .Retire("key-v1", Provenance(2), new HashSet<string>());

        var ex = Assert.Throws<InvalidOperationException>(() =>
            ring.Rotate(Key("key-v1", "old"), Provenance(3)));

        Assert.Contains("retired", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Unauthorized_operator_cannot_rotate_or_retire()
    {
        var operatorId = Guid.NewGuid();
        var manager = new AuthorizationContextIntegrityKeyRingManager(
            new AuthorizationContextIntegrityKeyRing(Key("key-v1", "old")),
            new AuthorizationKeyRotationAuthorizer(
                new Dictionary<Guid, string> { [operatorId] = "real-credential" }));

        Assert.Throws<UnauthorizedAccessException>(() =>
            manager.Rotate(Key("key-v2", "new"),
                new AuthorizationKeyRotationProvenance(operatorId, 1, "forged-credential")));

        Assert.Throws<UnauthorizedAccessException>(() =>
            manager.Rotate(Key("key-v2", "new"),
                new AuthorizationKeyRotationProvenance(Guid.NewGuid(), 1, "real-credential")));
    }

    [Fact]
    public void Concurrent_rotation_with_the_same_sequence_allows_only_one_commit()
    {
        var operatorId = Guid.NewGuid();
        var manager = new AuthorizationContextIntegrityKeyRingManager(
            new AuthorizationContextIntegrityKeyRing(Key("key-v1", "old")),
            new AuthorizationKeyRotationAuthorizer(
                new Dictionary<Guid, string> { [operatorId] = "rotation-credential" }));

        var successes = 0;
        var failures = 0;

        Parallel.For(0, 32, _ =>
        {
            try
            {
                manager.Rotate(Key("key-v2", "new"), Provenance(1, operatorId));
                Interlocked.Increment(ref successes);
            }
            catch (InvalidOperationException)
            {
                Interlocked.Increment(ref failures);
            }
        });

        Assert.Equal(1, successes);
        Assert.Equal(31, failures);
        Assert.Equal("key-v2", manager.Snapshot.ActiveKeyId);
        Assert.Equal(1, manager.Snapshot.LastRotationSequence);
    }

    [Fact]
    public void Rotation_publishes_an_atomic_immutable_snapshot()
    {
        var operatorId = Guid.NewGuid();
        var manager = new AuthorizationContextIntegrityKeyRingManager(
            new AuthorizationContextIntegrityKeyRing(Key("key-v1", "old")),
            new AuthorizationKeyRotationAuthorizer(
                new Dictionary<Guid, string> { [operatorId] = "rotation-credential" }));

        var before = manager.Snapshot;
        var after = manager.Rotate(Key("key-v2", "new"), Provenance(1, operatorId));

        Assert.NotSame(before, after);
        Assert.Equal("key-v1", before.ActiveKeyId);
        Assert.Equal(0, before.LastRotationSequence);
        Assert.Equal("key-v2", after.ActiveKeyId);
        Assert.Equal(1, after.LastRotationSequence);
    }

    [Fact]
    public void Invalid_rotation_provenance_fails_closed()
    {
        var ring = new AuthorizationContextIntegrityKeyRing(Key("key-v1", "old"));

        Assert.Throws<UnauthorizedAccessException>(() =>
            ring.Rotate(Key("key-v2", "new"),
                new AuthorizationKeyRotationProvenance(Guid.Empty, 1, "credential")));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ring.Rotate(Key("key-v2", "new"),
                new AuthorizationKeyRotationProvenance(Guid.NewGuid(), 0, "credential")));
    }

    private static AuthorizationContextIntegrityKey Key(string id, string material) =>
        new(id, SHA256.HashData(Encoding.UTF8.GetBytes("Foundgine-:" + material)));

    private static AuthorizationKeyRotationProvenance Provenance(long sequence) =>
        new(Guid.NewGuid(), sequence, "rotation-credential");

    private static AuthorizationKeyRotationProvenance Provenance(long sequence, Guid operatorId) =>
        new(operatorId, sequence, "rotation-credential");
}