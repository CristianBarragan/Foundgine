using Foundgine.Core.Mapping;

namespace Mapping.Benchmark.Aot;

/// <summary>Field-by-field comparison (records hold lists, so == is reference equality for them). Returns the first difference.</summary>
public static class StructuralEquality
{
    public static string? Diff(MappingModel a, MappingModel b)
    {
        var x = a.Mappings.OrderBy(m => m.Domain, StringComparer.Ordinal).ToList();
        var y = b.Mappings.OrderBy(m => m.Domain, StringComparer.Ordinal).ToList();
        if (x.Count != y.Count) return $"domain count {x.Count} != {y.Count}";
        for (var i = 0; i < x.Count; i++) { var d = MappingDiff(x[i], y[i]); if (d is not null) return d; }
        return null;
    }

    private static string? Seq<T>(string what, IReadOnlyList<T> a, IReadOnlyList<T> b, Func<T, T, string?> eq)
    {
        if (a.Count != b.Count) return $"{what}: count {a.Count} != {b.Count}";
        for (var i = 0; i < a.Count; i++) { var d = eq(a[i], b[i]); if (d is not null) return $"{what}[{i}]: {d}"; }
        return null;
    }

    private static string? MappingDiff(DomainMapping a, DomainMapping b)
    {
        if (a.Domain != b.Domain || a.ParentDomain != b.ParentDomain) return $"{a.Domain}: domain/parent differ";
        return Seq($"{a.Domain}.Tables", a.Tables, b.Tables, Table)
            ?? Seq($"{a.Domain}.Computed", a.Computed, b.Computed, (p, q) => p == q ? null : $"{p} != {q}")
            ?? Seq($"{a.Domain}.Scopes", a.Scopes, b.Scopes, (p, q) => p == q ? null : $"{p} != {q}")
            ?? Seq($"{a.Domain}.Relations", a.Relations, b.Relations, (p, q) => p == q ? null : $"{p} != {q}");
    }

    private static string? Table(TableBinding a, TableBinding b)
    {
        if (a.Table != b.Table || a.Role != b.Role || a.KeyColumn != b.KeyColumn || a.ParentTable != b.ParentTable || a.ForeignKey != b.ForeignKey
            || a.ParentKey != b.ParentKey || a.DiscriminatorColumn != b.DiscriminatorColumn || a.DiscriminatorValue != b.DiscriminatorValue)
            return $"table {a.Table} header differs";
        return Seq($"{a.Table}.Columns", a.Columns, b.Columns, (p, q) => p == q ? null : $"{p} != {q}");
    }
}
