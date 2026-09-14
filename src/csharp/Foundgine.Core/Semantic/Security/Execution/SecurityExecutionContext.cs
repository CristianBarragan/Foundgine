using Foundgine.Core.Semantic.Security.Warrants;

namespace Foundgine.Core.Semantic.Security.Execution;

/// <summary>
/// Untrusted-caller security context carried with a semantic request. The
/// warrant is evidence of authority; the engine still verifies it and checks
/// it against the resolved capability at execution time.
/// </summary>
public sealed record SecurityExecutionContext(
    SecurityWarrant Warrant,
    string Subject,
    string Audience,
    string? Tenant = null,
    string? ResourceScope = null)
{
    /// <summary>
    /// Stable authority partition used to prevent cross-warrant provider-plan cache reuse.
    /// The full warrant digest is intentional and includes nonce/signature, making the
    /// cache boundary conservative rather than attempting to infer authority equivalence.
    /// </summary>
    public string AuthorityCachePartition =>
        string.Join(
            '|',
            Escape(Subject),
            Escape(Audience),
            Escape(Tenant ?? "-"),
            Escape(ResourceScope ?? "-"),
            Escape(Warrant.Digest));

    /// <summary>
    /// Escapes the field delimiter and its own escape character so that field
    /// boundaries in the joined partition string cannot be forged by a field's
    /// own content (e.g. "agent|a" vs "agent" + "a|foundgine" would otherwise
    /// alias to the same partition string).
    /// </summary>
    private static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("|", "\\|");
}