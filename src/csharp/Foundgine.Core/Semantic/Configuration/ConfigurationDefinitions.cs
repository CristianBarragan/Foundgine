using Foundgine.Core.Semantic.Metadata;

namespace Foundgine.Core.Semantic.Configuration;

// Mutable state collected while configurations run. Nothing here is public:
// FoundgineModelCompiler turns it into the existing metadata records once every
// configuration has been applied, so configuration order never matters.

internal sealed class ModelDefinition(Type modelType)
{
    public Type ModelType { get; } = modelType;
    public string? EntityName { get; set; }
    public int? MinimumWeight { get; set; }
    public bool IsEvent { get; set; }
    public string? OccurredAtMember { get; set; }
    public List<AliasDeclaration> Aliases { get; } = [];

    /// <summary>Model member name to dimension name.</summary>
    public Dictionary<string, string> Dimensions { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, List<AliasDeclaration>> FieldAliases { get; } = new(StringComparer.Ordinal);
    public List<RelationshipDefinition> Relationships { get; } = [];
}

internal sealed class RelationshipDefinition(string member, Type memberType, Type targetModelType)
{
    public string Member { get; } = member;
    public Type MemberType { get; } = memberType;
    public Type TargetModelType { get; } = targetModelType;
    public string? InverseMember { get; set; }
    public bool InverseIsCollection { get; set; }
    /// <summary>Semantic name of the relationship; defaults to the navigation member name.</summary>
    public string? Name { get; set; }
    public List<AliasDeclaration> Aliases { get; } = [];
}

internal sealed class ColumnDefinition(MemberRef member)
{
    public string Member { get; } = member.Name;
    public Type Type { get; } = member.Type;
    public string? ColumnName { get; set; }
    public int? MaxLength { get; set; }
    public bool IsRequired { get; set; }
    public string EffectiveColumnName => ColumnName ?? Member;
}

internal sealed class PersistenceDefinition(Type entityType)
{
    public Type EntityType { get; } = entityType;
    public string? Table { get; set; }
    public string? Schema { get; set; }
    public MemberRef? Key { get; set; }
    public List<ColumnDefinition> Columns { get; } = [];

    /// <summary>Each entry is the member list of one index.</summary>
    public List<IReadOnlyList<MemberRef>> Indexes { get; } = [];

    public ColumnDefinition Column(MemberRef member)
    {
        var existing = Columns.Find(c => c.Member == member.Name);
        if (existing is not null)
            return existing;

        var created = new ColumnDefinition(member);
        Columns.Add(created);
        return created;
    }
}

internal sealed class EntityMapDefinition(Type modelType, Type entityType)
{
    public Type ModelType { get; } = modelType;
    public Type EntityType { get; } = entityType;
    public List<(MemberRef Model, MemberRef Entity)> Properties { get; } = [];
}

internal sealed class ConnectionDefinition(
    Type sourceModel,
    Type targetModel,
    string? navigation)
{
    public Type SourceModel { get; } = sourceModel;
    public Type TargetModel { get; } = targetModel;

    /// <summary>Optional member on <see cref="SourceModel"/> that disambiguates several relationships between the same two models.</summary>
    public string? Navigation { get; } = navigation;

    public Type? SourceEntity { get; set; }
    public Type? TargetEntity { get; set; }
    public bool ForeignKeyOnTarget { get; set; } = true;
    public MemberRef? ForeignKey { get; set; }
    public MemberRef? References { get; set; }
}

internal static class ConfigurationAlias
{
    public static AliasDeclaration Create(string name, int? weight)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new AliasDeclaration(name.Trim(), weight);
    }
}
