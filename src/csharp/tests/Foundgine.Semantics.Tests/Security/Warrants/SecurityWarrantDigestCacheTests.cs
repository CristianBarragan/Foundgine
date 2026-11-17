using Xunit;

namespace Foundgine.Core.Semantic.Security.Warrants;

public sealed class SecurityWarrantDigestCacheTests
{
    private static SecurityWarrant Warrant(string id = "w-1", byte[]? signature = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new SecurityWarrant(
            id, "issuer", "subject", "audience", [new CapabilityGrant("read", "query")],
            SecurityWarrantConstraints.Unrestricted, now.AddMinutes(-1), now.AddHours(1),
            "nonce-" + id, "key-1", null, signature ?? []);
    }

    [Fact]
    public void Digest_matches_a_fresh_canonical_computation_on_every_read()
    {
        var warrant = Warrant();
        var expected = SecurityWarrantCanonicalizer.Digest(warrant);

        Assert.Equal(expected, warrant.Digest);
        Assert.Equal(expected, warrant.Digest);
        Assert.Same(warrant.Digest, warrant.Digest);
    }

    [Fact]
    public void A_with_copy_that_changes_the_warrant_gets_its_own_digest()
    {
        var original = Warrant();
        var originalDigest = original.Digest;

        var changed = original with { Nonce = "different-nonce" };

        Assert.NotEqual(originalDigest, changed.Digest);
        Assert.Equal(SecurityWarrantCanonicalizer.Digest(changed), changed.Digest);
        Assert.Equal(originalDigest, original.Digest);
    }

    [Fact]
    public void A_with_copy_that_changes_delegation_gets_its_own_digest()
    {
        var original = Warrant();
        _ = original.Digest;

        var delegated = original with { ParentDigest = "PARENT", DelegationPath = ["ROOT", "PARENT"] };

        Assert.Equal(SecurityWarrantCanonicalizer.Digest(delegated), delegated.Digest);
        Assert.NotEqual(original.Digest, delegated.Digest);
    }

    [Fact]
    public void Signing_does_not_change_the_digest()
    {
        var unsigned = Warrant();
        var before = unsigned.Digest;

        var signed = unsigned with { Signature = [1, 2, 3] };

        Assert.Equal(before, signed.Digest);
    }

    [Fact]
    public void Reading_the_digest_does_not_change_equality_or_hash_codes()
    {
        var signature = new byte[] { 1, 2 };
        var a = Warrant("same", signature);
        var b = a with { };
        var c = a with { };
        _ = b.Digest;

        Assert.Equal(c, b);
        Assert.Equal(c.GetHashCode(), b.GetHashCode());
        Assert.Equal(a, b);
    }

    [Fact]
    public async Task Concurrent_reads_all_see_the_same_correct_digest()
    {
        var warrant = Warrant();
        var expected = SecurityWarrantCanonicalizer.Digest(warrant);

        var results = await Task.WhenAll(Enumerable.Range(0, 64).Select(_ => Task.Run(() => warrant.Digest)));

        Assert.All(results, x => Assert.Equal(expected, x));
    }
}
