namespace Foundgine.Core.Semantic.Security.Warrants;

/// <summary>Trust metadata for an identity that is permitted to issue delegated warrants.</summary>
public sealed record DelegationIssuerTrust(
    string Issuer,
    IReadOnlySet<string> SigningKeyIds,
    bool CanDelegate,
    string? Audience = null,
    IReadOnlySet<string>? AllowedTenants = null)
{
    /// <summary>Optional per-key lifecycle state. An omitted state preserves compatibility and treats listed keys as active.</summary>
    public IReadOnlyDictionary<string, DelegationIssuerKeyState> KeyStates { get; init; } =
        new Dictionary<string, DelegationIssuerKeyState>(StringComparer.Ordinal);

    public bool AllowsKey(string keyId) => SigningKeyIds.Contains(keyId);

    public DelegationIssuerKeyState GetKeyState(string keyId) =>
        KeyStates.TryGetValue(keyId, out var state) ? state : DelegationIssuerKeyState.Active;

    public bool AllowsAudience(string audience) =>
        Audience is null || StringComparer.Ordinal.Equals(Audience, audience);

    public bool AllowsTenant(string? tenant) =>
        tenant is null || AllowedTenants is null || AllowedTenants.Count == 0 || AllowedTenants.Contains(tenant);
}

public interface ISecurityWarrantDelegationTrustResolver
{
    DelegationIssuerTrust? Resolve(string issuer);
}

/// <summary>Validates that a warrant issuer is actually trusted to delegate the authority it holds.</summary>
public static class SecurityWarrantDelegationTrust
{
    public static void VerifyIssuer(
        SecurityWarrant parent,
        SecurityWarrant child,
        ISecurityWarrantDelegationTrustResolver trust,
        DateTimeOffset now,
        string? tenant = null)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(child);
        ArgumentNullException.ThrowIfNull(trust);

        var issuer = trust.Resolve(child.Issuer)
                     ?? throw new InvalidOperationException($"Delegation issuer '{child.Issuer}' is not trusted.");

        if (!issuer.CanDelegate)
            throw new InvalidOperationException(
                "Issuer is trusted for verification but is not authorized to delegate.");
        if (!issuer.AllowsKey(child.KeyId))
            throw new InvalidOperationException("Delegation was not signed by a trusted issuer key.");
        if (issuer.GetKeyState(child.KeyId) != DelegationIssuerKeyState.Active)
            throw new InvalidOperationException("Only an active issuer key may authorize a new delegation.");
        if (!issuer.AllowsAudience(child.Audience))
            throw new InvalidOperationException("Delegation audience is outside issuer trust scope.");
        if (!issuer.AllowsTenant(tenant))
            throw new InvalidOperationException("Delegation tenant is outside issuer trust scope.");
        if (!StringComparer.Ordinal.Equals(child.Issuer, parent.Subject))
            throw new InvalidOperationException("Delegation issuer must be the parent subject.");
        if (!parent.IsTimeValid(now))
            throw new InvalidOperationException("Parent authority is no longer valid.");
        if (child.ExpiresAt > parent.ExpiresAt)
            throw new InvalidOperationException("Delegation cannot extend parent validity.");
        if (child.IssuedAt < parent.IssuedAt)
            throw new InvalidOperationException("Delegation cannot predate parent authority.");
        if (!StringComparer.Ordinal.Equals(child.ParentDigest, parent.Digest))
            throw new InvalidOperationException("Delegation is not bound to the exact parent warrant.");
    }
}