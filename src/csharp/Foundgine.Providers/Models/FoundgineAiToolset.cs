using System.ComponentModel;
using System.Text.Json;
using Foundgine.Core.Execution;
using Foundgine.Core.Serialization;
using Foundgine.Core.Semantic.Security.Execution;
using Foundgine.Core.Semantic.Authorization;
using Foundgine.Runtime;
using Microsoft.Extensions.AI;
using ExecutionContext = Foundgine.Core.Execution.ExecutionContext;

namespace Foundgine.Providers.Models;

/// <summary>
/// Exposes Foundgine as a small, provider-neutral AI toolset.
/// The model can discover the authorized semantic surface and submit
/// provider-neutral read intent. Authentication/tenant context remains owned
/// by the host application and is never supplied by the model.
/// </summary>
public sealed class FoundgineAiToolset
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IFoundgine _foundgine;
    private readonly JsonReadIntentAdapter _adapter;
    private readonly Func<ExecutionContext> _contextFactory;
    private readonly Func<SecurityExecutionContext?> _securityContextFactory;

    public FoundgineAiToolset(
        IFoundgine foundgine,
        Func<ExecutionContext>? contextFactory = null,
        JsonReadIntentAdapter? adapter = null,
        Func<SecurityExecutionContext?>? securityContextFactory = null)
    {
        _foundgine = foundgine ?? throw new ArgumentNullException(nameof(foundgine));
        _contextFactory = contextFactory ?? (() => new ExecutionContext());
        _adapter = adapter ?? new JsonReadIntentAdapter();
        _securityContextFactory = securityContextFactory ?? (() => null);
    }

    /// <summary>
    /// Creates the tools that can be supplied to any Microsoft.Extensions.AI
    /// compatible chat client or agent.
    /// </summary>
    public IReadOnlyList<AIFunction> CreateTools() =>
    [
        AIFunctionFactory.Create(
            DescribeCapabilities,
            "foundgine_capabilities",
            "Discover the semantic entities, fields and relationships available to the current caller. This is descriptive only; execution re-checks authorization."),

        AIFunctionFactory.Create(
            ExecuteQueryAsync,
            "foundgine_query",
            "Execute a provider-neutral Foundgine read intent. The intent must use only entities, fields and relationships discovered through foundgine_capabilities. Do not provide tenant, identity or authorization context; the application supplies that.")
    ];

    [Description("Returns the canonical semantic capability contract available to the current caller.")]
    public string DescribeCapabilities()
    {
        var security = _securityContextFactory()
                       ?? throw new UnauthorizedAccessException(
                           "AI capability discovery requires a host-supplied SecurityExecutionContext. The model cannot supply identity, tenant, audience, or warrant context.");

        return JsonSerializer.Serialize(
            _foundgine.DescribeCapabilityContract(security),
            JsonOptions);
    }

    [Description("Executes a Foundgine read intent represented as JSON and returns rows plus execution evidence.")]
    public async Task<string> ExecuteQueryAsync(
        [Description(
            "JSON read intent with rootEntity, selections, optional filter/order/limit/offset/after. Do not include authentication, tenant or authorization context.")]
        string intentJson,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(intentJson))
            throw new ArgumentException("Intent JSON is required.", nameof(intentJson));

        var intent = _adapter.Parse(intentJson);
        var security = _securityContextFactory()
                       ?? throw new UnauthorizedAccessException(
                           "AI query execution requires a host-supplied SecurityExecutionContext. The model cannot supply identity, tenant, audience, or warrant context.");
        intent = intent with { Security = security };
        var result = await _foundgine.ExecuteAsync(
            intent,
            _contextFactory(),
            cancellationToken);

        return JsonSerializer.Serialize(ToToolResult(result), JsonOptions);
    }

    private static object ToToolResult(ExecutionResult result) => new
    {
        rows = result.Rows.Select(row => row.Values).ToArray(),
        pageInfo = result.PageInfo,
        evidence = result.Evidence
    };
}