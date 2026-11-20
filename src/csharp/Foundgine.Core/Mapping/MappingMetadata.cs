namespace Foundgine.Core.Mapping;

// Provider-neutral Domain -> Persistence mapping metadata. Domain types and persistence
// structures never reference each other; this metadata is the only link. Everything here is
// plain data (strings and lists) so runtime-built and generated metadata can be compared.

public enum TableRole { Root, Child, Join, Derived }

/// <summary>A domain member path ("Name", "Address.Street") bound to a column.</summary>
public sealed record ColumnBinding(string Path, string Column, string? ClrType = null);

/// <summary>
/// One persistence table used by a domain type. ParentTable/ForeignKey/ParentKey describe how the
/// table is reached from the table that owns the aggregate/base row (child, join, TPT-derived).
/// </summary>
public sealed record TableBinding(
    string Table,
    TableRole Role,
    string KeyColumn,
    IReadOnlyList<ColumnBinding> Columns,
    string? ParentTable = null,
    string? ForeignKey = null,
    string? ParentKey = null,
    string? DiscriminatorColumn = null,
    string? DiscriminatorValue = null);

public sealed record RelationBinding(string Path, string TargetDomain, string? JoinTable, string? LeftColumn, string? RightColumn);

/// <summary>A domain-level rule declared once: tenant boundary or ownership, resolved through the mapping.</summary>
public sealed record ScopeRule(string Kind, string Path, string Claim);

public sealed record DomainMapping(
    string Domain,
    IReadOnlyList<TableBinding> Tables,
    IReadOnlyList<string> Computed,
    IReadOnlyList<ScopeRule> Scopes,
    IReadOnlyList<RelationBinding> Relations,
    string? ParentDomain = null);
