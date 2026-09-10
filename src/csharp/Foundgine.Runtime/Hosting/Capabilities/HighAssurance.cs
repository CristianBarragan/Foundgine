using System.Collections.Concurrent;
using System.Security.Cryptography;
using Foundgine.Core.Semantic.Security.Warrants;
using Foundgine.Runtime;

namespace Foundgine.Runtime.Capabilities;

/// <summary>
/// Turns on Foundgine's warrant-backed "high assurance" execution path: requests carrying a
/// <see cref="SecurityWarrant"/> are verified against a trusted issuer, protected from replay, and
/// revalidated again immediately before the provider runs (see <see cref="FoundgineOptions.ExecutionAuthorizationRevalidator"/>,
/// consumed by <see cref="Foundgine.Runtime.FoundgineEngine"/>).
///
/// This capability only fills in whichever of <see cref="FoundgineOptions.WarrantKeyResolver"/>,
/// <see cref="FoundgineOptions.ExpectedWarrantIssuer"/> and <see cref="FoundgineOptions.WarrantReplayStore"/>
/// the host has not already configured, using an ephemeral in-process RSA key and a local
/// file-backed replay store as the default. <b>That default is for local development and samples
/// only.</b> A real deployment must set these three explicitly - typically a KMS/HSM-backed
/// <see cref="ISecurityWarrantKeyResolver"/> and a shared (not per-instance) replay store - before
/// calling <c>Enable&lt;HighAssurance&gt;()</c>; whichever of the three are already set are left
/// untouched.
/// </summary>
public sealed class HighAssurance : IFoundgineCapability
{
    /// <summary>Issuer name used by the development default when the host has not set one.</summary>
    public const string DevIssuer = "foundgine-dev";

    public static void Configure(FoundgineCapabilityContext context)
    {
        var options = context.Options;

        options.WarrantKeyResolver ??= new EphemeralDevWarrantKeyResolver();
        options.ExpectedWarrantIssuer ??= DevIssuer;
        options.WarrantReplayStore ??= new FileSecurityWarrantReplayStore(
            Path.Combine(Path.GetTempPath(), "foundgine-dev-warrant-replay.log"));
    }

    /// <summary>
    /// Generates and caches one RSA key pair per key id for the lifetime of the process.
    /// DEV ONLY: keys never leave the process and are lost on restart, which is exactly wrong for a
    /// real deployment where warrants must stay verifiable across restarts and across instances.
    /// </summary>
    private sealed class EphemeralDevWarrantKeyResolver : ISecurityWarrantKeyResolver
    {
        private readonly ConcurrentDictionary<string, RSA> _keys = new(StringComparer.Ordinal);

        public RSA Resolve(string keyId) => _keys.GetOrAdd(keyId, static _ => RSA.Create(2048));
    }
}

/// <summary>Fluent <c>Use</c>/<c>Disable</c> surface for <see cref="HighAssurance"/>.</summary>
public static class HighAssuranceFoundgineOptionsExtensions
{
    /// <summary>Enables <see cref="HighAssurance"/>. Equivalent to <c>options.Enable&lt;HighAssurance&gt;()</c>.</summary>
    public static FoundgineOptions UseHighAssurance(this FoundgineOptions options) =>
        options.Enable<HighAssurance>();

    public static FoundgineOptions DisableHighAssurance(this FoundgineOptions options) =>
        options.Disable<HighAssurance>();
}
