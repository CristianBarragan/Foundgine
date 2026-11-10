using System.Collections.Concurrent;
using Foundgine.Runtime;

namespace Foundgine.Providers.Gateway.Approvals;

/// <summary>
/// Single-process development store. It is intentionally not durable or shared across replicas;
/// production hosts must register a distributed implementation of IFoundgineGatewayApprovalStore.
/// </summary>
public sealed class InMemoryFoundgineGatewayApprovalStore : IAtomicFoundgineGatewayApprovalStore
{
    private sealed record Entry(MutationPlanApproval Approval, string Subject, string? Tenant, DateTimeOffset ExpiresAt);
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly TimeProvider _time;
    private readonly object _commitLock = new();
    private readonly List<FoundgineGatewayApprovalEvidence> _evidence = new();

    /// <summary>Development-only evidence view; this is not durable audit storage.</summary>
    public IReadOnlyList<FoundgineGatewayApprovalEvidence> Evidence { get { lock (_commitLock) return _evidence.ToArray(); } }

    public InMemoryFoundgineGatewayApprovalStore(TimeProvider? timeProvider = null) =>
        _time = timeProvider ?? TimeProvider.System;

    public ValueTask StoreAsync(string handleDigest, MutationPlanApproval approval, string subject, string? tenant,
        DateTimeOffset expiresAt, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RemoveExpired(_time.GetUtcNow());
        ArgumentException.ThrowIfNullOrWhiteSpace(handleDigest);
        ArgumentNullException.ThrowIfNull(approval);
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        if (!_entries.TryAdd(handleDigest, new Entry(approval, subject, tenant, expiresAt)))
            throw new InvalidOperationException("Approval handle collision.");
        return ValueTask.CompletedTask;
    }


    public async ValueTask StoreWithEvidenceAsync(string handleDigest, MutationPlanApproval approval,
        FoundgineGatewayApprovalEvidence evidence, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_commitLock)
        {
            // The in-memory implementation models a single atomic commit for development/tests only.
            StoreAsync(handleDigest, approval, evidence.Subject, evidence.Tenant, evidence.ExpiresAt, cancellationToken)
                .GetAwaiter().GetResult();
            _evidence.Add(evidence);
        }
        await ValueTask.CompletedTask;
    }

    public ValueTask<MutationPlanApproval?> TakeAsync(string handleDigest, string subject, string? tenant,
        DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_entries.TryGetValue(handleDigest, out var entry) ||
            !Same(entry.Subject, subject) || !Same(entry.Tenant, tenant))
            return ValueTask.FromResult<MutationPlanApproval?>(null);
        if (entry.ExpiresAt <= now)
        {
            ((ICollection<KeyValuePair<string, Entry>>)_entries).Remove(new KeyValuePair<string, Entry>(handleDigest, entry));
            return ValueTask.FromResult<MutationPlanApproval?>(null);
        }
        var removed = ((ICollection<KeyValuePair<string, Entry>>)_entries).Remove(new KeyValuePair<string, Entry>(handleDigest, entry));
        return ValueTask.FromResult(removed ? entry.Approval : null);
    }

    public ValueTask<bool> RevokeAsync(string handleDigest, string subject, string? tenant,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_entries.TryGetValue(handleDigest, out var entry) ||
            !Same(entry.Subject, subject) || !Same(entry.Tenant, tenant))
            return ValueTask.FromResult(false);
        return ValueTask.FromResult(((ICollection<KeyValuePair<string, Entry>>)_entries).Remove(new KeyValuePair<string, Entry>(handleDigest, entry)));
    }

    private void RemoveExpired(DateTimeOffset now)
    {
        foreach (var pair in _entries)
        {
            if (pair.Value.ExpiresAt <= now)
                ((ICollection<KeyValuePair<string, Entry>>)_entries).Remove(pair);
        }
    }

    private static bool Same(string? left, string? right) =>
        string.Equals(left, right, StringComparison.Ordinal);
}
