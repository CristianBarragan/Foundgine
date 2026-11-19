using System.Diagnostics;
using System.Text;
using Foundgine.Core.Execution;
using Foundgine.Providers.Storage.InMemory;
using ExecutionContext = Foundgine.Core.Execution.ExecutionContext;

namespace Foundgine.SupplyChain.Advanced.Intent;

/// <summary>
/// Adds provider evidence to the in-memory provider's results.
/// <para>
/// The in-memory provider returns rows only. The engine builds the execution receipt from the
/// provider's evidence (it adds the intent, plan and authorization fingerprints on top) and returns
/// the result untouched when there is none, so without this wrapper an intent run on the in-memory
/// fixture would come back without a receipt. The SQL and ORM providers attach this evidence
/// themselves.
/// </para>
/// </summary>
internal sealed class EvidencingProvider(IExecutionProvider inner) : IExecutionProvider
{
    public async Task<ExecutionResult> ExecuteAsync(
        ProviderPlan plan,
        ExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.GetTimestamp();
        var result = await inner.ExecuteAsync(plan, context, cancellationToken).ConfigureAwait(false);
        return WithEvidence(plan, result, started);
    }

    private static ExecutionResult WithEvidence(ProviderPlan plan, ExecutionResult result, long startedTimestamp)
    {
        if (result.Evidence is not null || plan is not InMemoryPlan memoryPlan)
            return result;

        var root = memoryPlan.IR.Root;
        var authorized = new List<int>();
        CollectAuthorized(root, authorized);

        // A deterministic description of the executed IR (a record's ToString would print list type names).
        var fingerprint = new StringBuilder(plan.Provider).Append('|');
        Describe(root, fingerprint);
        var text = fingerprint.ToString();

        var elapsedMilliseconds = (long)Stopwatch.GetElapsedTime(startedTimestamp).TotalMilliseconds;
        var evidence = ExecutionEvidenceFactory.Create(
            plan.Provider,
            ExecutionEvidenceFactory.Hash(text),
            authorized,
            result.Rows.Count,
            elapsedMilliseconds,
            text);
        return result with { Evidence = evidence };
    }

    private static void CollectAuthorized(ExecutionIRNode node, List<int> ids)
    {
        if (node.Authorization is not null) ids.Add(node.Id);
        foreach (var child in node.Children) CollectAuthorized(child, ids);
    }

    private static void Describe(ExecutionIRNode node, StringBuilder into)
    {
        into.Append('[').Append(node.Id).Append(':').Append(node.Operation)
            .Append(':').Append(node.EntityId.Value)
            .Append(":f=").AppendJoin(',', node.Fields.Select(f => f.Value))
            .Append(":via=").Append(node.ViaRelationship?.Value.ToString() ?? "-")
            .Append(":auth=").Append(node.Authorization is not null ? '1' : '0');
        foreach (var child in node.Children) Describe(child, into);
        into.Append(']');
    }
}
