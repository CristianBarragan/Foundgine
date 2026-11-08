using System.Text.RegularExpressions;
using Foundgine.Extensions.Temporal.Configuration;
using Foundgine.Extensions.Temporal.Contracts;
using Foundgine.Extensions.Temporal.Observability;
using Foundgine.Extensions.Temporal.Workflows;
using Temporalio.Api.Enums.V1;
using Temporalio.Client;
using Temporalio.Exceptions;

namespace Foundgine.Extensions.Temporal.Hosting;

/// <summary>Server-side catalog of processes. Clients pick a name; only the host defines what runs.</summary>
public sealed class FoundgineProcessCatalog
{
    private readonly Dictionary<string, FoundgineProcessDefinition> _definitions = new(StringComparer.Ordinal);

    public FoundgineProcessCatalog Add(FoundgineProcessDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (definition.Steps.Count == 0) throw new ArgumentException("A process needs at least one step.", nameof(definition));
        if (definition.Steps.Select(s => s.Name).Distinct(StringComparer.Ordinal).Count() != definition.Steps.Count)
            throw new ArgumentException("Step names must be unique within a process.", nameof(definition));
        _definitions[definition.Name] = definition;
        return this;
    }

    public bool TryGet(string name, out FoundgineProcessDefinition definition) =>
        _definitions.TryGetValue(name, out definition!);

    public IReadOnlyCollection<string> Names => _definitions.Keys;
}

public sealed record FoundgineWorkflowStartRequest(
    string TenantId,
    string Initiator,
    string Process,
    string BusinessKey,
    string PayloadJson,
    string? CorrelationId = null,
    Guid? SourceMessageId = null);

public sealed record FoundgineWorkflowStartResult(string WorkflowId, bool AlreadyExisted);

public interface IFoundgineWorkflowStarter
{
    Task<FoundgineWorkflowStartResult> StartAsync(FoundgineWorkflowStartRequest request, CancellationToken cancellationToken = default);
    string WorkflowId(string tenantId, string process, string businessKey);
}

public sealed partial class FoundgineWorkflowStarter(
    ITemporalClient client, FoundgineProcessCatalog catalog, FoundgineTemporalOptions options) : IFoundgineWorkflowStarter
{
    [GeneratedRegex(@"^[A-Za-z0-9._:\-]{1,128}$")]
    private static partial Regex SafeSegment();

    /// <summary>
    /// Deterministic id: the same tenant/process/business key always maps to the same workflow, which is what makes
    /// at-least-once outbox delivery and HTTP retries idempotent. Segments are validated so ids cannot cross tenants.
    /// </summary>
    public string WorkflowId(string tenantId, string process, string businessKey)
    {
        foreach (var (name, value) in new[] { ("tenant", tenantId), ("process", process), ("businessKey", businessKey) })
            if (!SafeSegment().IsMatch(value))
                throw new ArgumentException($"Invalid {name}: 1–128 chars of letters, digits, '.', '_', ':' or '-'.");
        return $"{options.WorkflowIdPrefix}/{tenantId}/{process}/{businessKey}";
    }

    public async Task<FoundgineWorkflowStartResult> StartAsync(
        FoundgineWorkflowStartRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!catalog.TryGet(request.Process, out var definition))
            throw new ArgumentException($"Unknown process '{request.Process}'.", nameof(request));

        var id = WorkflowId(request.TenantId, request.Process, request.BusinessKey);
        var input = new FoundgineProcessInput(
            request.TenantId, request.Initiator, request.BusinessKey, request.PayloadJson, definition,
            options.ToActivityPolicy(), request.CorrelationId, request.SourceMessageId);

        try
        {
            await client.StartWorkflowAsync(
                (FoundgineProcessWorkflow wf) => wf.RunAsync(input),
                new WorkflowOptions(id, options.TaskQueue)
                {
                    // Running duplicate → attach; finished-successfully duplicate → rejected (caught below).
                    IdConflictPolicy = WorkflowIdConflictPolicy.UseExisting,
                    IdReusePolicy = WorkflowIdReusePolicy.AllowDuplicateFailedOnly
                }).ConfigureAwait(false);
            FoundgineTemporalDiagnostics.RecordStart(request.Process, "started");
            return new(id, false);
        }
        catch (WorkflowAlreadyStartedException)
        {
            FoundgineTemporalDiagnostics.RecordStart(request.Process, "duplicate");
            return new(id, true);
        }
    }
}
