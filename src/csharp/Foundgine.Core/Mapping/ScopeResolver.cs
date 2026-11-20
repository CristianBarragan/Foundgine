using System.Text.Json;

namespace Foundgine.Core.Mapping;

public sealed record JoinHop(string Table, string ForeignKey, string ParentTable, string ParentKey);

/// <summary>
/// "Apply claim <see cref="Claim"/> == <see cref="Table"/>.<see cref="Column"/> when querying <see cref="QueriedTable"/>",
/// reaching <see cref="Table"/> through <see cref="Via"/> (empty when the column is on the queried table itself).
/// </summary>
public sealed record ScopeFilter(string Kind, string Claim, string QueriedTable, string Table, string Column, IReadOnlyList<JoinHop> Via)
{
    public override string ToString() =>
        (Via.Count == 0 ? "" : string.Join(" ", Via.Select(h => $"{h.Table}.{h.ForeignKey}->{h.ParentTable}.{h.ParentKey}")) + " | ")
        + $"{QueriedTable}: {Table}.{Column} = @{Claim}";
}

public sealed class MappingScopeException(string message) : InvalidOperationException(message);

/// <summary>
/// Resolves a domain type's tenant/ownership rules to concrete table/column filters through the mapping.
/// The rules are declared once on the domain; only the mapping knows where the columns live.
/// Fails closed: if a table cannot be scoped, resolution throws instead of returning an unfiltered table.
/// </summary>
public static class ScopeResolver
{
    public static IReadOnlyList<ScopeFilter> Resolve(MappingModel model, string domain)
    {
        var mapping = model.Get(domain);
        var rules = RulesOf(model, mapping);
        if (rules.Count == 0)
            throw new MappingScopeException($"{domain} declares no tenant or owner scope (and neither does its base/aggregate root).");

        var result = new List<ScopeFilter>();
        foreach (var table in mapping.Tables)
            foreach (var rule in rules)
            {
                var (owner, column, hops) = Locate(model, table.Table, rule.Path, []);
                result.Add(new ScopeFilter(rule.Kind, rule.Claim, table.Table, owner, column, hops));
            }
        return result;
    }

    /// <summary>
    /// Filters needed to follow a many-to-many link safely: the join table is scoped through the source side and the
    /// target entity is scoped by its own domain rules, so a link row cannot join rows across a tenant/owner boundary.
    /// </summary>
    public static IReadOnlyList<ScopeFilter> ResolveLink(MappingModel model, string domain, string path)
    {
        var rel = model.Get(domain).Relations.FirstOrDefault(r => r.Path == path && r.JoinTable is not null)
                  ?? throw new MappingScopeException($"{domain}.{path} is not a mapped many-to-many relationship.");
        var filters = Resolve(model, domain).Where(f => f.QueriedTable == rel.JoinTable).ToList();
        filters.AddRange(Resolve(model, rel.TargetDomain));
        return filters;
    }

    /// <summary>True when the domain type, its base or its aggregate root declares a tenant/owner rule.</summary>
    public static bool DeclaresScope(MappingModel model, string domain) => RulesOf(model, model.Get(domain)).Count > 0;

    private static IReadOnlyList<ScopeRule> RulesOf(MappingModel model, DomainMapping mapping)
    {
        for (var m = mapping; ; )
        {
            if (m.Scopes.Count > 0) return m.Scopes;
            if (m.ParentDomain is null) return [];
            m = model.Get(m.ParentDomain);
        }
    }

    private static (string Table, string Column, List<JoinHop> Hops) Locate(MappingModel model, string table, string path, HashSet<string> seen)
    {
        if (!seen.Add(table)) throw new MappingScopeException($"Cyclic parent chain at table '{table}'.");

        // Shared persistence: every domain type bound to this table contributes its columns.
        var bindings = model.Mappings.SelectMany(m => m.Tables).Where(t => t.Table == table).ToList();
        var direct = bindings.SelectMany(t => t.Columns).FirstOrDefault(c => c.Path == path);
        if (direct is not null) return (table, direct.Column, []);

        var link = bindings.FirstOrDefault(t => t.ParentTable is not null);
        if (link is null)
            throw new MappingScopeException($"Table '{table}' has no column bound to '{path}' and no parent to inherit scope from; refusing to leave it unscoped.");

        var (owner, column, hops) = Locate(model, link.ParentTable!, path, seen);
        hops.Insert(0, new JoinHop(table, link.ForeignKey!, link.ParentTable!, link.ParentKey!));
        return (owner, column, hops);
    }
}

/// <summary>Serializable form of the mapping. Stands in for generated (AOT) metadata: same data, no reflection.</summary>
public static class MappingSnapshot
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    public static string Serialize(MappingModel model) =>
        JsonSerializer.Serialize(model.Mappings.OrderBy(m => m.Domain, StringComparer.Ordinal).ToList(), Options);

    public static MappingModel Deserialize(string json)
    {
        var model = new MappingModel();
        foreach (var m in JsonSerializer.Deserialize<List<DomainMapping>>(json, Options)!) model.Add(m);
        return model;
    }
}
