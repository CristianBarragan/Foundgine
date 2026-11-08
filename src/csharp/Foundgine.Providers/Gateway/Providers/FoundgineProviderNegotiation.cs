using System.Numerics;
using System.Text;

namespace Foundgine.Providers.Gateway.Providers;

/// <summary>An application requirement: some provider of <paramref name="Kind"/> must declare all of <paramref name="Required"/>.</summary>
public sealed record FoundgineProviderRequirement(
    FoundgineProviderKind Kind,
    FoundgineProviderCapability Required,
    string? Reason = null);

public sealed record FoundgineProviderNegotiationFailure(
    FoundgineProviderKind Kind,
    FoundgineProviderCapability Missing,
    string? ClosestProviderId,
    string? Reason);

public sealed record FoundgineProviderNegotiationResult(
    IReadOnlyList<FoundgineProviderNegotiationFailure> Failures)
{
    public bool IsSatisfied => Failures.Count == 0;

    public void ThrowIfUnsatisfied()
    {
        if (!IsSatisfied) throw new FoundgineProviderNegotiationException(this);
    }
}

public sealed class FoundgineProviderNegotiationException : InvalidOperationException
{
    public FoundgineProviderNegotiationResult Result { get; }

    public FoundgineProviderNegotiationException(FoundgineProviderNegotiationResult result)
        : base(BuildMessage(result)) => Result = result;

    private static string BuildMessage(FoundgineProviderNegotiationResult result)
    {
        var sb = new StringBuilder("Foundgine provider capability negotiation failed:");
        foreach (var f in result.Failures)
        {
            sb.Append("\n - ").Append(f.Kind).Append(": missing ")
              .Append(FoundgineProviderCapabilityNames.Describe(f.Missing));
            sb.Append(f.ClosestProviderId is null
                ? " (no provider registered)"
                : $" (closest provider '{f.ClosestProviderId}')");
            if (!string.IsNullOrWhiteSpace(f.Reason)) sb.Append(" — ").Append(f.Reason);
        }
        return sb.ToString();
    }
}

/// <summary>
/// Pure, allocation-light compatibility check. A single provider must satisfy a whole requirement:
/// capabilities of different providers are never merged (Kafka + PostgreSQL does not make a transactional outbox).
/// </summary>
public static class FoundgineProviderNegotiator
{
    public static FoundgineProviderNegotiationResult Negotiate(
        IReadOnlyCollection<IFoundgineProvider> providers,
        IReadOnlyCollection<FoundgineProviderRequirement> requirements)
    {
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(requirements);

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var provider in providers)
            if (!ids.Add(provider.Id))
                throw new ArgumentException($"Duplicate provider id '{provider.Id}'.", nameof(providers));

        var failures = new List<FoundgineProviderNegotiationFailure>();
        foreach (var requirement in requirements)
        {
            IFoundgineProvider? closest = null;
            var closestMissing = requirement.Required;
            var satisfied = false;
            foreach (var provider in providers)
            {
                if (provider.Kind != requirement.Kind) continue;
                var missing = requirement.Required & ~provider.Capabilities;
                if (missing == FoundgineProviderCapability.None) { satisfied = true; break; }
                if (closest is null ||
                    BitOperations.PopCount((ulong)missing) < BitOperations.PopCount((ulong)closestMissing))
                {
                    closest = provider;
                    closestMissing = missing;
                }
            }
            if (!satisfied)
                failures.Add(new FoundgineProviderNegotiationFailure(
                    requirement.Kind, closestMissing, closest?.Id, requirement.Reason));
        }
        return new FoundgineProviderNegotiationResult(failures);
    }
}
