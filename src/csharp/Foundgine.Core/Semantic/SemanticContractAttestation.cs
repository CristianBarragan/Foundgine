namespace Foundgine.Core.Semantic;

/// <summary>
/// Verifies that a semantic model is the contract expected by a generated or
/// otherwise trusted semantic artifact. The fingerprint is an integrity
/// identifier only; it does not grant authorization.
/// </summary>
public static class SemanticContractAttestation
{
    public static bool Matches(SemanticModel model, string expectedFingerprint)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedFingerprint);

        return string.Equals(
            model.ContractFingerprint,
            Normalize(expectedFingerprint),
            StringComparison.Ordinal);
    }

    public static void EnsureMatches(SemanticModel model, string expectedFingerprint)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedFingerprint);

        var expected = Normalize(expectedFingerprint);
        if (string.Equals(model.ContractFingerprint, expected, StringComparison.Ordinal))
            return;

        throw new InvalidOperationException(
            $"Semantic contract attestation failed. Expected fingerprint '{expected}', " +
            $"but the runtime semantic model has fingerprint '{model.ContractFingerprint}'.");
    }

    private static string Normalize(string fingerprint)
    {
        const string prefix = "sha256:";
        return fingerprint.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? fingerprint[prefix.Length..].ToLowerInvariant()
            : fingerprint.ToLowerInvariant();
    }
}