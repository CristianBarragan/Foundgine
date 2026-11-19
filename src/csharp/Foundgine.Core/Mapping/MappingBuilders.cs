using System.Linq.Expressions;
using System.Reflection;

namespace Foundgine.Core.Mapping;

public sealed class MappingModel
{
    private readonly List<DomainMapping> _maps = [];
    public IReadOnlyList<DomainMapping> Mappings => _maps;

    public MappingModel Map<T>(Action<DomainMapBuilder<T>> configure)
    {
        var builder = new DomainMapBuilder<T>();
        configure(builder);
        _maps.AddRange(builder.BuildAll());
        return this;
    }

    public MappingModel Add(DomainMapping mapping) { _maps.Add(mapping); return this; }

    public DomainMapping Get(string domain) =>
        _maps.Find(m => m.Domain == domain) ?? throw new KeyNotFoundException($"No mapping for domain type '{domain}'.");
}

public sealed class DomainMapBuilder<T>
{
    private readonly List<TableBuilder<T>> _tables = [];
    private readonly List<string> _computed = [];
    private readonly List<ScopeRule> _scopes = [];
    private readonly List<RelationBinding> _relations = [];
    private readonly List<DomainMapping> _extra = [];
    private readonly List<TableBinding> _joins = [];
    private string? _parent;

    /// <summary>Declares that this domain type derives from (or is an aggregate part of) another mapped domain type.</summary>
    public DomainMapBuilder<T> Base<TBase>() { _parent = typeof(TBase).Name; return this; }

    public TableBuilder<T> Table(string table, string key, TableRole role = TableRole.Root)
    {
        var t = new TableBuilder<T>(table, key, role);
        _tables.Add(t);
        return t;
    }

    public DomainMapBuilder<T> Computed(Expression<Func<T, object?>> member) { _computed.Add(MemberPath.Of(member)); return this; }
    public DomainMapBuilder<T> Tenant(Expression<Func<T, object?>> member, string claim = "tenant_id") { _scopes.Add(new("tenant", MemberPath.Of(member), claim)); return this; }
    public DomainMapBuilder<T> Owner(Expression<Func<T, object?>> member, string claim = "customer_id") { _scopes.Add(new("owner", MemberPath.Of(member), claim)); return this; }

    /// <summary>A part of this aggregate stored in its own table, reached through a foreign key to this type's root table.</summary>
    public DomainMapBuilder<T> Child<TChild>(Expression<Func<T, object?>> nav, string table, string key, string foreignKey, Action<TableBuilder<TChild>> columns)
    {
        var root = RootTable();
        var tb = new TableBuilder<TChild>(table, key, TableRole.Child).Parent(root.Table, foreignKey, root.KeyColumn);
        columns(tb);
        _extra.Add(new DomainMapping(typeof(TChild).Name, [tb.Build()], [], [], [], typeof(T).Name));
        _relations.Add(new RelationBinding(MemberPath.Of(nav), typeof(TChild).Name, null, null, null));
        return this;
    }

    /// <summary>Many-to-many through a join table; the join table is scoped through this (left) side.</summary>
    public DomainMapBuilder<T> ManyToMany<TTarget>(Expression<Func<T, object?>> nav, string joinTable, string leftColumn, string rightColumn)
    {
        var root = RootTable();
        _joins.Add(new TableBinding(joinTable, TableRole.Join, "", [], root.Table, leftColumn, root.KeyColumn));
        _relations.Add(new RelationBinding(MemberPath.Of(nav), typeof(TTarget).Name, joinTable, leftColumn, rightColumn));
        return this;
    }

    private TableBuilder<T> RootTable() =>
        _tables.Find(t => t.Role is TableRole.Root) ?? throw new InvalidOperationException($"{typeof(T).Name} needs a root table before Child/ManyToMany.");

    internal IEnumerable<DomainMapping> BuildAll()
    {
        var computed = _computed.ToHashSet(StringComparer.Ordinal);
        var tables = _tables.Select(t => t.Build(computed)).Concat(_joins).ToList();
        yield return new DomainMapping(typeof(T).Name, tables, _computed.ToList(), _scopes, _relations, _parent);
        foreach (var e in _extra) yield return e;
    }
}

public sealed class TableBuilder<T>
{
    private readonly string _table, _key;
    private readonly List<ColumnBinding> _columns = [];
    private string? _parentTable, _fk, _parentKey, _discCol, _discVal;

    internal TableBuilder(string table, string key, TableRole role) { _table = table; _key = key; Role = role; }
    internal TableRole Role { get; }
    internal string Table => _table;
    internal string KeyColumn => _key;

    /// <summary>Maps every public scalar property to a column of the same name (or <paramref name="naming"/>(name)). Nested value objects are not touched.</summary>
    public TableBuilder<T> Convention(Func<string, string>? naming = null)
    {
        foreach (var p in typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            if (p.CanRead && IsScalar(p.PropertyType) && !_columns.Exists(c => c.Path == p.Name))
                _columns.Add(new(p.Name, naming?.Invoke(p.Name) ?? p.Name, p.PropertyType.AssemblyQualifiedName));
        return this;
    }

    /// <summary>Explicit binding. Covers renames ("Name" -> "FullName") and flattening/owned value objects ("Address.Street" -> "street").</summary>
    public TableBuilder<T> Column(Expression<Func<T, object?>> path, string column)
    {
        var p = MemberPath.Of(path);
        _columns.RemoveAll(c => c.Path == p);
        _columns.Add(new(p, column, MemberPath.TypeOf(path).AssemblyQualifiedName));
        return this;
    }

    public TableBuilder<T> Parent(string parentTable, string foreignKey, string parentKey) { _parentTable = parentTable; _fk = foreignKey; _parentKey = parentKey; return this; }
    public TableBuilder<T> Discriminator(string column, string value) { _discCol = column; _discVal = value; return this; }

    internal TableBinding Build(ISet<string>? computed = null)
    {
        var columns = _columns.Where(c => computed is null || !computed.Contains(c.Path)).ToList();
        // The key is a PHYSICAL column name. For a root table it must be one of the bound columns,
        // otherwise the primary-key reference would silently point at a column no field maps to.
        if (Role is TableRole.Root && _parentTable is null && !columns.Exists(c => c.Column == _key))
            throw new InvalidOperationException(
                $"{typeof(T).Name}: key '{_key}' of table '{_table}' is not a mapped column (key is the physical column name; mapped: {string.Join(", ", columns.Select(c => c.Column))}).");
        return new(_table, Role, _key, columns, _parentTable, _fk, _parentKey, _discCol, _discVal);
    }

    private static bool IsScalar(Type t)
    {
        t = Nullable.GetUnderlyingType(t) ?? t;
        return t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal) || t == typeof(DateTime) || t == typeof(DateTimeOffset) || t == typeof(Guid);
    }
}

internal static class MemberPath
{
    public static Type TypeOf<T>(Expression<Func<T, object?>> e) =>
        e.Body is UnaryExpression { NodeType: ExpressionType.Convert } u ? u.Operand.Type : e.Body.Type;

    public static string Of<T>(Expression<Func<T, object?>> e)
    {
        var parts = new Stack<string>();
        Expression? cur = e.Body;
        while (cur is not null)
        {
            switch (cur)
            {
                case UnaryExpression { NodeType: ExpressionType.Convert } u: cur = u.Operand; break;
                case MemberExpression m: parts.Push(m.Member.Name); cur = m.Expression; break;
                case ParameterExpression: cur = null; break;
                default: throw new ArgumentException($"Only simple member paths are supported: {e}");
            }
        }
        return string.Join('.', parts);
    }
}
