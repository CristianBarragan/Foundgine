using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Foundgine.Core.Execution;

/// <summary>
/// Immutable, provider-neutral evidence that a semantic execution occurred.
/// The receipt deliberately contains fingerprints rather than request/result
/// payloads so it can be persisted or transported without copying domain data.
/// </summary>
public sealed record ExecutionReceipt(
    string RequestId,
    string Status,
    string SemanticModelVersion,
    int CapabilityContractVersion,
    int CapabilityVersion,
    int IntentVersion,
    int PlanVersion,
    string IntentFingerprint,
    string PlanFingerprint,
    string AuthorizationFingerprint,
    string Provider,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    IReadOnlyList<int> AffectedNodeIds,
    IReadOnlyList<string> Effects,
    string ResultFingerprint,
    string? ApprovalId = null,
    string? ApprovedBy = null,
    DateTimeOffset? ApprovedAt = null,
    string? WarrantId = null,
    string? WarrantDigest = null,
    string? SecurityInvariantDigest = null)
{
    public long ElapsedMilliseconds => Math.Max(0, (long)(CompletedAt - StartedAt).TotalMilliseconds);
}

public static class ExecutionReceiptFactory
{
    public static ExecutionReceipt Create(
        string requestId,
        ExecutionEvidence evidence,
        string resultFingerprint,
        IEnumerable<int> affectedNodeIds,
        IEnumerable<string> effects,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        int capabilityContractVersion,
        int capabilityVersion,
        int intentVersion,
        int planVersion,
        string semanticModelVersion,
        string? approvalId = null,
        string? approvedBy = null,
        DateTimeOffset? approvedAt = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentException.ThrowIfNullOrWhiteSpace(resultFingerprint);

        return new ExecutionReceipt(
            requestId,
            "succeeded",
            semanticModelVersion,
            capabilityContractVersion,
            capabilityVersion,
            intentVersion,
            planVersion,
            evidence.IntentFingerprint ??
            throw new InvalidOperationException("Execution evidence is missing an intent fingerprint."),
            evidence.PlanFingerprint,
            evidence.AuthorizationFingerprint ??
            throw new InvalidOperationException("Execution evidence is missing an authorization fingerprint."),
            evidence.Provider,
            startedAt,
            completedAt,
            affectedNodeIds.Distinct().OrderBy(x => x).ToArray(),
            effects.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray(),
            resultFingerprint,
            approvalId,
            approvedBy,
            approvedAt,
            evidence.WarrantId,
            evidence.WarrantDigest,
            evidence.SecurityInvariantDigest);
    }

    public static string FingerprintResult(ExecutionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var canonical = new StringBuilder(512);
        foreach (var row in result.Rows)
        {
            foreach (var pair in row.Values.OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                canonical.Append(pair.Key).Append('=');
                AppendValue(canonical, pair.Value);
                canonical.Append('|');
            }

            canonical.Append("row;");
        }

        if (result.PageInfo is { } page)
        {
            canonical.Append("page[")
                .Append(page.StartCursor).Append('|')
                .Append(page.EndCursor).Append('|')
                .Append(page.HasNextPage).Append('|')
                .Append(page.HasPreviousPage).Append(']');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }

    private static void AppendValue(StringBuilder builder, object? value)
    {
        if (value is null)
        {
            builder.Append("null");
            return;
        }

        switch (value)
        {
            case JsonElement json:
                builder.Append(json.GetRawText());
                break;
            case byte[] bytes:
                builder.Append(Convert.ToHexString(bytes));
                break;
            case string text:
                builder.Append(text.Length).Append(':').Append(text);
                break;
            case IFormattable formattable:
                builder.Append(formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture));
                break;
            default:
                builder.Append(value);
                break;
        }
    }
}