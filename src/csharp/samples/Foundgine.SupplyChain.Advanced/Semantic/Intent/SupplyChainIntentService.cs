using System.Collections.Concurrent;
using System.Globalization;
using System.Security;
using System.Text.RegularExpressions;
using Foundgine.Core.Abstractions;
using Foundgine.Core.Execution;
using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.Authorization;
using Foundgine.Core.Semantic.Intent;
using Foundgine.Core.Semantic.Planning;
using Foundgine.Core.Semantic.Resolution;
using Foundgine.Core.Serialization;
using Foundgine.Providers.Storage.InMemory;
using Foundgine.Runtime;
using Foundgine.SupplyChain.Advanced.Authorization;
using Foundgine.SupplyChain.Advanced.Data;
using Foundgine.SupplyChain.Advanced.Semantics;
using Microsoft.Extensions.DependencyInjection;
using ExecutionContext = Foundgine.Core.Execution.ExecutionContext;

namespace Foundgine.SupplyChain.Advanced.Intent;

/// <summary>
/// Transport-neutral canonical intent boundary for the Advanced sample (the C# counterpart of the Java
/// <c>SupplyChainIntentService</c>).
/// <para>
/// The request is the canonical, provider-neutral <see cref="ReadIntent"/> JSON that core's
/// <see cref="JsonReadIntentAdapter"/> understands: a root entity, selections (fields and relationship
/// names), and optionally a filter, ordering and paging. It names <em>what is wanted</em> in the semantic
/// vocabulary (entity, field and relationship names or aliases), never tables, joins or SQL.
/// </para>
/// <para>
/// Everything else is Foundgine's job and happens inside a real engine: name resolution, path resolution
/// across relationships, authorization (entity, field, relationship and row-level predicates), planning,
/// execution and a receipt. The caller's identity is <em>not</em> part of the intent; the host supplies it
/// as an <see cref="IntentCaller"/>, which selects the authorization policy and supplies the tenant to the
/// row-level predicate. So one intent returns different results for different callers, and nothing in
/// the intent can widen what the caller may see.
/// </para>
/// Methods: <see cref="ExecuteAsync"/> runs the intent as the caller; <see cref="Explain"/> dry-runs it,
/// showing how it resolves and what the caller's policy leaves; <see cref="Capabilities"/> says what the
/// caller may read. Data is a snapshot of the <see cref="SupplyChainData"/> fixture taken at construction.
/// </summary>
public sealed class SupplyChainIntentService
{
    /// <summary>The execution-context key the tenant predicate reads (<c>context.TenantId</c>).</summary>
    public const string TenantContextKey = "context.TenantId";

    private static readonly SemanticModel Model = SupplyChainSemanticModel.Model;
    private static readonly SemanticContractSnapshot Contract = Model.Freeze().CreateSnapshot();
    private static readonly TimeSpan ExecutionDeadline = TimeSpan.FromSeconds(10);

    private static readonly Regex EntityIdText =
        new(@"EntityId \{ Value = (\d+) \}", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly JsonReadIntentAdapter _adapter = new();
    private readonly InMemoryCompiler _compiler;
    private readonly EvidencingProvider _provider;

    // One engine per (tenant, role). The key space is bounded: tenants are validated against the data
    // and roles are an enum.
    private readonly ConcurrentDictionary<IntentCaller, IFoundgine> _engines = new();

    public SupplyChainIntentService(SupplyChainData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var dataSet = SupplyChainRows.Load(data);
        _compiler = new InMemoryCompiler(SupplyChainSemanticModel.Metadata, dataSet);
        _provider = new EvidencingProvider(new InMemoryExecutionProvider(SupplyChainSemanticModel.Metadata, dataSet));

        var tenants = new HashSet<string>(StringComparer.Ordinal);
        foreach (var supplier in data.Suppliers) tenants.Add(supplier.TenantId);
        foreach (var warehouse in data.Warehouses) tenants.Add(warehouse.TenantId);
        KnownTenants = tenants;
    }

    /// <summary>Tenants present in the data; anything else is refused by <see cref="IntentCaller.Resolve"/>.</summary>
    public IReadOnlySet<string> KnownTenants { get; }

    // ------------------------------------------------------------------ execute

    public async Task<Dictionary<string, object?>> ExecuteAsync(
        IntentCaller caller,
        string intentJson,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        // Everything that can be wrong with the intent itself is settled here and reported as invalid_intent.
        var request = Compile(intentJson);

        var context = new ExecutionContext(new Dictionary<string, object?> { [TenantContextKey] = caller.TenantId })
        {
            DeadlineUtc = DateTimeOffset.UtcNow + ExecutionDeadline
        };
        var result = await Engine(caller).ExecuteAsync(request, context, cancellationToken).ConfigureAwait(false);
        return ExecutionResponse(caller, result);
    }

    private static Dictionary<string, object?> ExecutionResponse(IntentCaller caller, ExecutionResult result)
    {
        var rows = result.Rows.Select(ShapeRow).ToList();
        return new Dictionary<string, object?>
        {
            ["caller"] = CallerMap(caller),
            ["rowCount"] = rows.Count,
            ["rows"] = rows,
            ["pageInfo"] = result.PageInfo,
            ["evidence"] = Evidence(result.Evidence),
            ["receipt"] = Receipt(result.Receipt)
        };
    }

    /// <summary>Keys rows by semantic name (<c>Entity.Field</c>) instead of provider-facing identities.</summary>
    private static Dictionary<string, object?> ShapeRow(ExecutionRow row)
    {
        var shaped = new Dictionary<string, object?>();
        foreach (var (key, value) in row.EffectiveCells.OrderBy(cell => cell.Key.NodeId))
        {
            var entity = Model.Get(key.EntityId);
            var name = $"{entity.Name}.{FieldName(entity, key.FieldId)}";
            if (shaped.ContainsKey(name)) name = $"{name}@{key.NodeId}";
            shaped[name] = Plain(value);
        }

        return shaped;
    }

    private static object? Plain(object? value) => value switch
    {
        null or string or bool => value,
        Enum e => e.ToString(),
        IFormattable f when value is DateOnly or DateTime or DateTimeOffset or TimeOnly =>
            f.ToString("O", CultureInfo.InvariantCulture),
        byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal => value,
        _ => Convert.ToString(value, CultureInfo.InvariantCulture)
    };

    private static Dictionary<string, object?>? Evidence(ExecutionEvidence? evidence) =>
        evidence is null
            ? null
            : new Dictionary<string, object?>
            {
                ["provider"] = evidence.Provider,
                ["planFingerprint"] = evidence.PlanFingerprint,
                ["authorizedNodeIds"] = evidence.AuthorizedNodeIds,
                ["rowsReturned"] = evidence.RowsReturned,
                ["elapsedMilliseconds"] = evidence.ElapsedMilliseconds,
                ["intentFingerprint"] = evidence.IntentFingerprint,
                ["authorizationFingerprint"] = evidence.AuthorizationFingerprint
            };

    private static Dictionary<string, object?>? Receipt(ExecutionReceipt? receipt) =>
        receipt is null
            ? null
            : new Dictionary<string, object?>
            {
                ["requestId"] = receipt.RequestId,
                ["status"] = receipt.Status,
                ["provider"] = receipt.Provider,
                ["intentFingerprint"] = receipt.IntentFingerprint,
                ["planFingerprint"] = receipt.PlanFingerprint,
                ["authorizationFingerprint"] = receipt.AuthorizationFingerprint,
                ["startedAt"] = receipt.StartedAt.ToString("O", CultureInfo.InvariantCulture),
                ["completedAt"] = receipt.CompletedAt.ToString("O", CultureInfo.InvariantCulture),
                ["elapsedMilliseconds"] = receipt.ElapsedMilliseconds
            };

    // ------------------------------------------------------------------ explain

    /// <summary>
    /// Plans and authorizes the intent without executing it. Shows how the intent's names resolved
    /// (including the relationship path Foundgine chose), which requested fields the caller's policy
    /// removed, and the authorized plan.
    /// </summary>
    public Dictionary<string, object?> Explain(IntentCaller caller, string intentJson)
    {
        ArgumentNullException.ThrowIfNull(caller);
        var request = Compile(intentJson);
        var graph = ResolveGraph(request);
        var inspection = Engine(caller).DryRun(request).Inspection;

        var planNodes = new Dictionary<int, PlanInspectionNode>();
        Flatten(inspection.Nodes, planNodes);

        var resolution = new List<Dictionary<string, object?>>();
        var pruned = new List<string>();
        foreach (var node in graph.Nodes)
        {
            var entity = Model.Get(node.EntityId);
            var step = new Dictionary<string, object?>
            {
                ["node"] = node.Id,
                ["entity"] = entity.Name
            };
            if (node.ViaRelationship is not null && node.ParentId is { } parentId &&
                graph.Nodes.FirstOrDefault(n => n.Id == parentId) is { } parent)
                step["via"] = RelationshipName(parent.EntityId, node);
            step["fields"] = node.Fields.Select(field => $"{entity.Name}.{FieldName(entity, field)}").ToList();
            resolution.Add(step);

            if (!planNodes.TryGetValue(node.Id, out var kept)) continue;
            foreach (var field in node.Fields.Where(field => !kept.FieldIds.Contains(field.Value)))
                pruned.Add($"{entity.Name}.{FieldName(entity, field)}");
        }

        var effects = inspection.Effects;
        return new Dictionary<string, object?>
        {
            ["caller"] = CallerMap(caller),
            ["contract"] = Model.ContractFingerprint,
            ["resolution"] = resolution,
            ["authorization"] = new Dictionary<string, object?> { ["prunedFields"] = pruned },
            ["plan"] = new Dictionary<string, object?>
            {
                ["fingerprint"] = inspection.PlanFingerprint,
                ["root"] = inspection.Nodes.Select(PlanNode).ToList(),
                ["effects"] = new Dictionary<string, object?>
                {
                    ["hasWrites"] = effects.HasWrites,
                    ["hasExternalSideEffects"] = effects.HasExternalSideEffects,
                    ["affectedPlanNodes"] = effects.AffectedPlanNodes,
                    ["effects"] = effects.Effects
                }
            }
        };
    }

    private static void Flatten(IEnumerable<PlanInspectionNode> nodes, Dictionary<int, PlanInspectionNode> into)
    {
        foreach (var node in nodes)
        {
            into[node.NodeId] = node;
            Flatten(node.Children, into);
        }
    }

    private static Dictionary<string, object?> PlanNode(PlanInspectionNode node)
    {
        var entity = EntityByValue(node.EntityId);
        return new Dictionary<string, object?>
        {
            ["node"] = node.NodeId,
            ["operation"] = node.Operation,
            ["entity"] = entity?.Name ?? node.EntityId.ToString(CultureInfo.InvariantCulture),
            ["fields"] = node.FieldIds
                .Select(id => entity is null
                    ? id.ToString(CultureInfo.InvariantCulture)
                    : $"{entity.Name}.{FieldName(entity, new FieldId(id))}")
                .ToList(),
            ["authorizationApplied"] = node.AuthorizationApplied,
            ["children"] = node.Children.Select(PlanNode).ToList()
        };
    }

    // ------------------------------------------------------------------ capabilities

    /// <summary>
    /// What the caller may do, as the authorization policy sees it: per entity, whether it is readable
    /// (and whether a row-level predicate scopes it), and which fields and relationships are visible.
    /// Descriptive only: execution evaluates the policy again.
    /// </summary>
    public Dictionary<string, object?> Capabilities(IntentCaller caller)
    {
        ArgumentNullException.ThrowIfNull(caller);
        var entities = new List<Dictionary<string, object?>>();
        foreach (var capability in Engine(caller).DescribeCapabilities().Entities)
        {
            var entity = Model.Get(capability.EntityId);
            entities.Add(new Dictionary<string, object?>
            {
                ["entity"] = capability.Name,
                ["aliases"] = entity.EffectiveAliases.Select(alias => alias.Name).ToList(),
                ["read"] = new Dictionary<string, object?>
                {
                    ["access"] = AccessName(capability.Read),
                    // CONDITIONAL means a row-level predicate (here the tenant predicate) scopes the rows.
                    ["scopedByPredicate"] = capability.Read.Access == AuthorizationAccess.Conditional,
                    ["fields"] = capability.Fields.Where(f => f.Read.IsAllowed).Select(f => f.Name).ToList(),
                    ["relationships"] = capability.Relationships.Where(r => r.Read.IsAllowed).Select(r => r.Name)
                        .ToList()
                },
                ["write"] = new Dictionary<string, object?> { ["access"] = AccessName(capability.Write) }
            });
        }

        return new Dictionary<string, object?>
        {
            ["caller"] = CallerMap(caller),
            ["intentShape"] = new Dictionary<string, object?>
            {
                ["rootEntity"] = "an entity name or alias",
                ["selections"] = "[{field} | {relationship, children}]",
                ["filter"] = "optional: field, relationship (exists/all/any) and boolean combinations",
                ["order"] = "optional",
                ["limit"] = "optional",
                ["offset"] = "optional",
                ["identity"] = "never part of the intent; supplied by the host as the caller"
            },
            ["entities"] = entities
        };
    }

    private static string AccessName(AuthorizationDecision decision) =>
        decision.Access.ToString().ToUpperInvariant();

    // ------------------------------------------------------------------ failures

    /// <summary>An error as a transport should report it.</summary>
    public sealed record Failure(int Status, string Type, string Message);

    /// <summary>Classifies an exception from this service into a transport-neutral failure.</summary>
    public static Failure Classify(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        var t = error;
        while (t is AggregateException { InnerException: { } inner }) t = inner;

        var message = string.IsNullOrWhiteSpace(t.Message) ? t.GetType().Name : t.Message;
        // SemanticAuthorizationException is an InvalidOperationException, so it is checked first.
        return t switch
        {
            SemanticAuthorizationException or SecurityException =>
                new Failure(403, "forbidden", SemanticNames(message)),
            InvalidIntentException or ArgumentException =>
                new Failure(400, "invalid_intent", message),
            TimeoutException or OperationCanceledException =>
                new Failure(504, "timeout", "The intent did not finish within its deadline."),
            // Never leak internals for anything unclassified.
            _ => new Failure(500, "internal_error", "The intent could not be executed.")
        };
    }

    /// <summary>The engine names entities by identity in denials; report the semantic name instead.</summary>
    private static string SemanticNames(string message) =>
        EntityIdText.Replace(message, m =>
            EntityByValue(ulong.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))?.Name ?? "unknown");

    /// <summary>The intent body was not a valid canonical read intent.</summary>
    public sealed class InvalidIntentException(string message, Exception? inner = null)
        : Exception(message, inner);

    // ------------------------------------------------------------------ helpers

    /// <summary>Parses, compiles and resolves an intent, so any defect in it is an <see cref="InvalidIntentException"/>.</summary>
    private SemanticRequest Compile(string intentJson)
    {
        try
        {
            var request = new ReadIntentCompiler(Model).Compile(_adapter.Parse(intentJson));
            ResolveGraph(request);
            return request;
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException or KeyNotFoundException)
        {
            throw new InvalidIntentException(e.Message, e);
        }
    }

    private static SemanticGraph ResolveGraph(SemanticRequest request) =>
        new SemanticRequestResolver(Contract).Resolve(request);

    private IFoundgine Engine(IntentCaller caller) =>
        _engines.GetOrAdd(caller, c =>
        {
            var services = new ServiceCollection();
            services.AddFoundgine(Model, SupplyChainAuthorization.Create(c.TenantId, c.Role));
            services.AddSingleton<IProviderPlanCompiler>(_compiler);
            services.AddSingleton<IExecutionProvider>(_provider);
            return services.BuildServiceProvider().GetRequiredService<IFoundgine>();
        });

    private static Dictionary<string, object?> CallerMap(IntentCaller caller) =>
        new() { ["tenant"] = caller.TenantId, ["role"] = caller.RoleName };

    private static string FieldName(SemanticEntity entity, FieldId id) =>
        entity.Fields.FirstOrDefault(f => f.Id == id)?.Name ?? id.Value.ToString(CultureInfo.InvariantCulture);

    private static string RelationshipName(EntityId parentEntity, SemanticGraphNode node)
    {
        var parent = Model.Get(parentEntity);
        var name = parent.Relationships.FirstOrDefault(r => r.Id == node.ViaRelationship)?.Name ?? "?";
        return $"{parent.Name}.{name}";
    }

    private static SemanticEntity? EntityByValue(ulong value) =>
        Model.Entities.FirstOrDefault(e => e.Id.Value == value);
}
