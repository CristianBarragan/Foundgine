using System.Security.Cryptography;
using System.Text;

namespace Foundgine.Core.Semantic.Security.Warrants;

/// <summary>
/// Validates the complete delegation chain represented by a set of warrants.
/// The validator deliberately operates on the exact warrant objects that are about
/// to be used; it does not infer ancestry from an untrusted path alone.
/// </summary>
public static class SecurityWarrantDelegationChainValidator
{
    public const int MaxDelegationDepth = SecurityWarrantAttenuator.MaxDelegationDepth;

    public static void Validate(
        IReadOnlyList<SecurityWarrant> chain,
        DateTimeOffset now,
        string? expectedRootDigest = null)
    {
        ArgumentNullException.ThrowIfNull(chain);
        if (chain.Count == 0)
            throw new InvalidOperationException("A delegation chain must contain at least one warrant.");
        if (chain.Count > MaxDelegationDepth + 1)
            throw new InvalidOperationException("Delegation chain exceeds the maximum supported depth.");

        var root = chain[0];
        if (root.ParentId is not null || root.ParentDigest is not null || root.DelegationPath.Count != 0)
            throw new InvalidOperationException("The root warrant cannot contain delegation ancestry.");
        if (!root.IsTimeValid(now))
            throw new InvalidOperationException("The root warrant is not currently valid.");

        if (expectedRootDigest is not null &&
            !StringComparer.Ordinal.Equals(root.Digest, expectedRootDigest))
            throw new InvalidOperationException("Delegation chain root does not match the expected root digest.");

        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        var seenDigests = new HashSet<string>(StringComparer.Ordinal);
        AddUnique(root, seenIds, seenDigests);

        for (var index = 1; index < chain.Count; index++)
        {
            var parent = chain[index - 1];
            var child = chain[index];

            if (!parent.IsTimeValid(now))
                throw new InvalidOperationException("A delegation ancestor is no longer currently valid.");
            if (!child.IsTimeValid(now))
                throw new InvalidOperationException("A delegated warrant is not currently valid.");
            if (!StringComparer.Ordinal.Equals(child.ParentId, parent.Id))
                throw new InvalidOperationException("Delegation chain contains a parent-id substitution or reorder.");
            if (!StringComparer.Ordinal.Equals(child.ParentDigest, parent.Digest))
                throw new InvalidOperationException("Delegation chain contains a parent-digest substitution.");
            if (!StringComparer.Ordinal.Equals(child.Issuer, parent.Subject))
                throw new InvalidOperationException("Delegation chain issuer does not match the parent subject.");
            if (child.DelegationDepth != index)
                throw new InvalidOperationException("Delegation depth does not match the supplied chain position.");
            if (child.DelegationPath.Count != index)
                throw new InvalidOperationException("Delegation path length does not match the chain position.");

            for (var pathIndex = 0; pathIndex < index; pathIndex++)
            {
                var expected = chain[pathIndex].Digest;
                if (!StringComparer.Ordinal.Equals(child.DelegationPath[pathIndex], expected))
                    throw new InvalidOperationException(
                        "Delegation path contains a splice, reorder, or substituted ancestor.");
            }

            if (child.DelegationPath.Distinct(StringComparer.Ordinal).Count() != child.DelegationPath.Count)
                throw new InvalidOperationException("Delegation chain contains a repeated ancestor.");

            AddUnique(child, seenIds, seenDigests);
        }
    }

    private static void AddUnique(SecurityWarrant warrant, HashSet<string> ids, HashSet<string> digests)
    {
        if (!ids.Add(warrant.Id))
            throw new InvalidOperationException("Delegation chain contains a repeated warrant id.");
        if (!digests.Add(warrant.Digest))
            throw new InvalidOperationException("Delegation chain contains a repeated warrant digest.");
    }

    /// <summary>
    /// Returns a deterministic digest of the complete ordered chain. This digest is
    /// execution-time evidence and must not be included in semantic/compiled plan identity.
    /// </summary>
    public static string ChainDigest(IReadOnlyList<SecurityWarrant> chain)
    {
        ValidateShape(chain);
        using var sha = SHA256.Create();
        using var stream = new MemoryStream();
        foreach (var warrant in chain)
        {
            var digestBytes = Convert.FromHexString(warrant.Digest);
            WriteLengthPrefixed(stream, digestBytes);
        }

        return Convert.ToHexString(sha.ComputeHash(stream.ToArray()));
    }

    private static void ValidateShape(IReadOnlyList<SecurityWarrant> chain)
    {
        ArgumentNullException.ThrowIfNull(chain);
        if (chain.Count == 0)
            throw new InvalidOperationException("A delegation chain must contain at least one warrant.");
        if (chain.Count > MaxDelegationDepth + 1)
            throw new InvalidOperationException("Delegation chain exceeds the maximum supported depth.");
    }

    private static void WriteLengthPrefixed(Stream stream, ReadOnlySpan<byte> value)
    {
        Span<byte> length = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, value.Length);
        stream.Write(length);
        stream.Write(value);
    }
}