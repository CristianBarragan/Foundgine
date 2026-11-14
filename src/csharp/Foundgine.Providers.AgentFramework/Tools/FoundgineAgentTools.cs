using System.ComponentModel;
using System.Text.Json;
using Foundgine.Core.Semantic.Security.Execution;
using Foundgine.Providers.Tools.MCP;
using Foundgine.Runtime;
using Microsoft.Extensions.AI;

namespace Foundgine.Providers.AgentFramework.Tools;

/// <summary>
/// The model-facing Foundgine tool surface for Microsoft Agent Framework agents.
/// </summary>
/// <remarks>
/// Reads delegate to <see cref="FoundgineMcpTools"/> so MAF, MCP and GraphQL callers share one validated path.
/// Mutations never expose <c>approve</c> as a model-callable tool (a model must not approve its own plan):
/// <c>foundgine_mutation_execute</c> is wrapped in <see cref="ApprovalRequiredAIFunction"/>, so MAF pauses the run
/// for a human decision, and the approver identity recorded by Foundgine is the host-resolved subject.
/// </remarks>
public sealed class FoundgineAgentTools
{
    public const string CapabilitiesTool = "foundgine_capabilities";
    public const string QueryTool = "foundgine_query";
    public const string QuerySemanticTool = "foundgine_query_semantic";
    public const string MutationDryRunTool = "foundgine_mutation_dry_run";
    public const string MutationExecuteTool = "foundgine_mutation_execute";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly FoundgineMcpTools _reads;
    private readonly IFoundgineMutations? _mutations;
    private readonly ISecurityExecutionContextProvider _security;
    private readonly bool _openIntentAvailable;

    /// <param name="reads">Shared read path (capabilities, semantic and open-intent queries).</param>
    /// <param name="securityContextProvider">Host-owned caller identity. Missing context fails closed.</param>
    /// <param name="mutations">Mutation engine; required only when mutations are exposed.</param>
    /// <param name="openIntentAvailable">True when lexical grounding is configured, enabling <c>foundgine_query</c>.</param>
    public FoundgineAgentTools(
        FoundgineMcpTools reads,
        ISecurityExecutionContextProvider securityContextProvider,
        IFoundgineMutations? mutations = null,
        bool openIntentAvailable = false)
    {
        _reads = reads ?? throw new ArgumentNullException(nameof(reads));
        _security = securityContextProvider ?? throw new ArgumentNullException(nameof(securityContextProvider));
        _mutations = mutations;
        _openIntentAvailable = openIntentAvailable;
    }

    /// <summary>Creates the tools to hand to a MAF <c>ChatClientAgent</c>.</summary>
    /// <param name="includeMutations">Adds dry-run and approval-gated execute. Requires a mutation engine.</param>
    public IReadOnlyList<AITool> CreateTools(bool includeMutations = false)
    {
        var tools = new List<AITool>
        {
            AIFunctionFactory.Create(_reads.DescribeCapabilities, CapabilitiesTool,
                "Discover the semantic entities, fields and relationships available to the current caller. Descriptive only; execution re-checks authorization."),

            AIFunctionFactory.Create(_reads.ExecuteSemanticQueryAsync, QuerySemanticTool,
                "Execute a structured, provider-neutral Foundgine read intent (rootEntity, selections, optional filter/order/limit/offset/after) using only entities and fields from foundgine_capabilities. Never supply tenant, identity, authorization, SQL or connection details.")
        };

        if (_openIntentAvailable)
            tools.Add(AIFunctionFactory.Create(_reads.ExecuteQueryAsync, QueryTool,
                "Execute a natural-language or JSON open intent; Foundgine grounds it against the semantic contract before authorization and execution. Never supply tenant, identity, authorization, SQL or connection details."));

        if (includeMutations)
        {
            if (_mutations is null)
                throw new InvalidOperationException(
                    "Mutations were requested but no IFoundgineMutations is registered. Configure FoundgineOptions.MutationSchema and MutationProvider.");

            tools.Add(AIFunctionFactory.Create(DryRun, MutationDryRunTool,
                "Validate and authorize a mutation without executing it. Returns planFingerprint and declared effects. Call this before foundgine_mutation_execute."));

            // The only way a mutation runs: MAF pauses for a human approval of this exact call.
            tools.Add(new ApprovalRequiredAIFunction(AIFunctionFactory.Create(ExecuteAsync, MutationExecuteTool,
                "Execute a mutation that was dry-run first. Requires human approval. Pass the same mutationJson and the planFingerprint returned by foundgine_mutation_dry_run.")));
        }

        return tools;
    }

    [Description("Validates and authorizes a mutation without executing it.")]
    public string DryRun(
        [Description("JSON mutation: { \"operations\": [ { entity, kind, fields, filter?, returnFields?, conflictFields? } ] }.")]
        string mutationJson)
    {
        var mutations = RequireMutations();
        var request = WithHostSecurity(MutationJsonAdapter.Parse(mutationJson), "mutation dry run");
        return JsonSerializer.Serialize(mutations.DryRun(request), JsonOptions);
    }

    [Description("Executes a previously dry-run mutation after human approval.")]
    public async Task<string> ExecuteAsync(
        [Description("The exact mutation JSON that was dry-run.")] string mutationJson,
        [Description("The planFingerprint returned by foundgine_mutation_dry_run for this mutation.")] string planFingerprint,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(planFingerprint))
            throw new ArgumentException("planFingerprint from foundgine_mutation_dry_run is required.", nameof(planFingerprint));

        var mutations = RequireMutations();
        var security = _security.RequireSecurityExecutionContext("MAF", "mutation execution");
        var request = MutationJsonAdapter.Parse(mutationJson) with { Security = security };

        // Re-plan under the current caller. A forged, stale or swapped mutation no longer matches what was reviewed.
        var current = mutations.DryRun(request);
        if (!string.Equals(current.PlanFingerprint, planFingerprint, StringComparison.Ordinal))
            throw new InvalidOperationException(
                "planFingerprint does not match the mutation as currently planned. Run foundgine_mutation_dry_run again.");

        cancellationToken.ThrowIfCancellationRequested();

        // approvedBy is the host-resolved subject, never a model-supplied value.
        var approval = mutations.Approve(request, security.Subject);
        var result = await mutations.ExecuteApprovedAsync(approval, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return JsonSerializer.Serialize(result, JsonOptions);
    }

    private SemanticMutationRequest WithHostSecurity(SemanticMutationRequest request, string operation) =>
        request with { Security = _security.RequireSecurityExecutionContext("MAF", operation) };

    private IFoundgineMutations RequireMutations() => _mutations ??
        throw new InvalidOperationException(
            "Foundgine mutation execution is not configured. Configure FoundgineOptions.MutationSchema and MutationProvider.");
}
