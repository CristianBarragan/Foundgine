using System.Diagnostics;
using System.Globalization;
using Foundgine.Core.Abstractions;
using Foundgine.Core.Execution;
using ExecutionContext = Foundgine.Core.Execution.ExecutionContext;

namespace Foundgine.Providers.Orm;

/// <summary>
/// ORM-agnostic executor. The EF Core adapter wraps it with its own <see cref="IOrmQuerySource"/>.
/// Each node of the plan is one query (children are fetched with the parent keys of the page, never per row),
/// then the node results are joined in memory into the flat "joined row" shape the result materializer expects.
/// </summary>
public class OrmExecutionProvider : IExecutionProvider
{
    private readonly string _provider;
    private readonly IOrmQuerySource _source;
    private readonly OrmRegistry _registry;

    public OrmExecutionProvider(string provider, IOrmQuerySource source, OrmRegistry registry)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public async Task<ExecutionResult> ExecuteAsync(
        ProviderPlan plan,
        ExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(context);
        if (plan is not OrmPlan ormPlan || ormPlan.Provider != _provider)
            throw new ArgumentException($"The {_provider} provider requires an {_provider} OrmPlan.", nameof(plan));
        if (ormPlan.AuthorizationBinding is null)
            throw new InvalidOperationException(
                $"The {_provider} provider refuses to execute a provider plan without authorization provenance.");
        if (ormPlan.IR.AuthorizationBinding is null ||
            ormPlan.AuthorizationBinding != ormPlan.IR.AuthorizationBinding)
            throw new InvalidOperationException(
                $"The {_provider} provider refuses to execute a plan whose authorization provenance " +
                "does not match its ExecutionIR.");

        // Equal bindings are not enough: the IR itself must be the instance the plan was compiled from.
        if (!ReferenceEquals(ormPlan.BoundIR, ormPlan.IR))
            throw new InvalidOperationException(
                $"The {_provider} provider refuses to execute a plan whose ExecutionIR is not the one it was compiled from.");

        context.EnsureWithinDeadline();
        using var cts = context.CreateDeadlineCancellationSource(cancellationToken);
        var stopwatch = Stopwatch.StartNew();

        var root = ormPlan.IR.Root;
        var tree = await LoadAsync(root, null, null, context, cts.Token).ConfigureAwait(false);

        var rows = new List<ExecutionRow>();
        for (var i = 0; i < tree.Rows.Count; i++)
        {
            foreach (var joined in Expand(tree, i, isRoot: true))
                rows.Add(new ExecutionRow(joined.Values, joined.Cells));
        }

        context.EnsureWithinDeadline();
        stopwatch.Stop();

        var fingerprint = BuildFingerprint(ormPlan);
        var authorized = new List<int>();
        CollectAuthorized(root, authorized);

        var evidence = ExecutionEvidenceFactory.Create(
            _provider,
            ExecutionEvidenceFactory.Hash(fingerprint),
            authorized,
            rows.Count,
            stopwatch.ElapsedMilliseconds,
            fingerprint);

        return new ExecutionResult(rows, tree.PageInfo, evidence);
    }

    // ---------------------------------------------------------------- loading

    private sealed class NodeData
    {
        public required ExecutionIRNode Node { get; init; }
        public IReadOnlyList<IReadOnlyDictionary<FieldId, object?>> Rows { get; init; } = [];
        public ExecutionPageInfo? PageInfo { get; init; }
        public List<ChildData> Children { get; } = [];
    }

    private sealed class ChildData
    {
        public required OrmLink Link { get; init; }
        public required NodeData Data { get; init; }

        /// <summary>Child row indexes grouped by the normalized value of the child key field.</summary>
        public Dictionary<string, List<int>> ByKey { get; } = new(StringComparer.Ordinal);
    }

    private async Task<NodeData> LoadAsync(
        ExecutionIRNode node,
        OrmLink? incoming,
        IReadOnlyList<object?>? keys,
        ExecutionContext context,
        CancellationToken cancellationToken)
    {
        context.EnsureWithinDeadline();
        var map = _registry.GetMap(node.EntityId);

        // Key fields are read only to join; they are emitted only if the node itself selected them.
        var extra = new List<FieldId>();
        if (incoming is not null) extra.Add(incoming.ChildField);
        var links = new List<OrmLink>(node.Children.Count);
        foreach (var child in node.Children)
        {
            var link = _registry.GetLink(child.ViaRelationship!.Value, node.EntityId);
            links.Add(link);
            if (!extra.Contains(link.ParentField)) extra.Add(link.ParentField);
        }

        var fetch = incoming is not null && keys is { Count: 0 }
            ? new OrmFetch([], null) // no parent keys: nothing can match, skip the query
            : await map.FetchAsync(node, incoming is null, extra, incoming?.ChildField, keys,
                _source, context, cancellationToken).ConfigureAwait(false);

        var data = new NodeData { Node = node, Rows = fetch.Rows, PageInfo = fetch.PageInfo };
        for (var c = 0; c < node.Children.Count; c++)
        {
            var link = links[c];
            var parentKeys = new List<object?>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in data.Rows)
            {
                var key = row[link.ParentField];
                if (Normalize(key) is { } normalized && seen.Add(normalized)) parentKeys.Add(key);
            }

            var childData = await LoadAsync(node.Children[c], link, parentKeys, context, cancellationToken)
                .ConfigureAwait(false);
            var child = new ChildData { Link = link, Data = childData };
            for (var i = 0; i < childData.Rows.Count; i++)
            {
                if (Normalize(childData.Rows[i][link.ChildField]) is not { } k) continue;
                if (!child.ByKey.TryGetValue(k, out var list)) child.ByKey[k] = list = [];
                list.Add(i);
            }

            data.Children.Add(child);
        }

        return data;
    }

    /// <summary>Type-insensitive join key (int 1 and long 1 are the same key; null never joins).</summary>
    private static string? Normalize(object? value) => value switch
    {
        null => null,
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString()
    };

    // ---------------------------------------------------------------- row assembly

    private sealed class Joined
    {
        public Dictionary<ExecutionCellKey, object?> Cells { get; } = new();
        public Dictionary<string, object?> Values { get; } = new(StringComparer.Ordinal);
    }

    /// <summary>
    /// Joined rows for one node row: a LEFT JOIN cross product. A parent with no children yields one row with
    /// no cells for that child (the materializer treats a missing identity as "no child"); a parent with N and
    /// M rows in two sibling children yields N*M rows, exactly like the SQL provider's joins. Root values keep
    /// plain field names; child values are prefixed "{nodeId}." so they cannot collide.
    /// </summary>
    private List<Joined> Expand(NodeData data, int rowIndex, bool isRoot)
    {
        var node = data.Node;
        var row = data.Rows[rowIndex];

        var own = new Joined();
        foreach (var fieldId in node.Fields)
        {
            var value = row[fieldId];
            own.Cells[new ExecutionCellKey(node.Id, node.EntityId, fieldId)] = value;
            own.Values[(isRoot ? "" : node.Id + ".") + _registry.GetMap(node.EntityId).FieldName(fieldId)] = value;
        }

        var combos = new List<Joined> { own };
        foreach (var child in data.Children)
        {
            var matches = new List<Joined>();
            if (Normalize(row[child.Link.ParentField]) is { } key && child.ByKey.TryGetValue(key, out var indexes))
            {
                foreach (var i in indexes) matches.AddRange(Expand(child.Data, i, isRoot: false));
            }

            if (matches.Count == 0) continue; // left join: keep the row, child cells stay absent

            var next = new List<Joined>(combos.Count * matches.Count);
            foreach (var left in combos)
            foreach (var right in matches)
            {
                var merged = new Joined();
                foreach (var kv in left.Cells) merged.Cells[kv.Key] = kv.Value;
                foreach (var kv in right.Cells) merged.Cells[kv.Key] = kv.Value;
                foreach (var kv in left.Values) merged.Values[kv.Key] = kv.Value;
                foreach (var kv in right.Values) merged.Values[kv.Key] = kv.Value;
                next.Add(merged);
            }

            combos = next;
        }

        return combos;
    }

    // ---------------------------------------------------------------- evidence

    private static void CollectAuthorized(ExecutionIRNode node, List<int> ids)
    {
        if (node.Authorization is not null) ids.Add(node.Id);
        foreach (var child in node.Children) CollectAuthorized(child, ids);
    }

    private static string BuildFingerprint(OrmPlan plan)
    {
        var sb = new System.Text.StringBuilder(plan.Provider);
        Append(sb, plan.IR.Root);
        return sb.ToString();
    }

    private static void Append(System.Text.StringBuilder sb, ExecutionIRNode n)
    {
        var fields = string.Join(",", n.Fields.Select(x => x.Value));
        var o = n.QueryOptions;
        var order = string.Join(",", (o?.EffectiveOrder ?? []).Select(x => $"{x.Field.Value}:{x.Direction}"));
        sb.Append($"|node={n.Id}|entity={n.EntityId.Value}|via={n.ViaRelationship?.Value}|fields={fields}|order={order}")
          .Append($"|limit={o?.Limit}|offset={o?.Offset}|after={o?.HasCursor}|filter={o?.Filter is not null}")
          .Append($"|auth={n.Authorization is not null}");
        if (n.Children.Count == 0) return;
        sb.Append('[');
        foreach (var child in n.Children) Append(sb, child);
        sb.Append(']');
    }
}
