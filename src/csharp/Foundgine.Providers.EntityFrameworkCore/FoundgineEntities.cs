#pragma warning disable IL2026, IL2055, IL2060, IL2070, IL2072, IL2075, IL3050
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using Foundgine.Core.Semantic.Metadata;
using Foundgine.Core.Semantic.Query;
using Microsoft.Extensions.DependencyInjection;

namespace Foundgine.Providers.EntityFrameworkCore;

/// <summary>
/// Name-addressed, fully generic access to every entity of the model: the same CRUD and semantic read for any
/// entity, with no per-entity code. Closed generic <see cref="FoundgineEntity{T}"/> instances are created from the
/// metadata. Mechanical like the repository: authorize before calling (semantic reads are additionally authorized
/// by the Foundgine planner). Scoped, because the repositories are.
/// </summary>
public sealed class FoundgineEntities(IServiceProvider services, FoundgineEfModel model)
{
    public IEnumerable<string> Names =>
        model.Entities.Where(e => e.ClrType is not null).Select(e => e.Name);

    public bool Has(string name) => Find(name) is not null;

    [RequiresUnreferencedCode("Resolves closed generic repositories and entities dynamically by CLR type.")]
    [RequiresDynamicCode("Creates closed generic types dynamically by CLR type.")]
    public IFoundgineEntity Entity(string name)
    {
        var meta = Find(name) ?? throw new KeyNotFoundException($"Unknown entity '{name}'.");
        var clr = meta.ClrType!;
        var repository = services.GetRequiredService(typeof(IFoundgineRepository<>).MakeGenericType(clr));
        return (IFoundgineEntity)Activator.CreateInstance(
            typeof(FoundgineEntity<>).MakeGenericType(clr), meta, repository,
            services.GetRequiredService<FoundgineEfReader>())!;
    }

    /// <summary>Typed access to the same entity through the generic repository.</summary>
    public IFoundgineRepository<T> Repository<[DynamicallyAccessedMembers(FoundgineEfOptions.EfEntityMembers)] T>()
        where T : class =>
        services.GetRequiredService<IFoundgineRepository<T>>();

    private EntityMetadata? Find(string name) =>
        model.Entities.FirstOrDefault(e =>
            e.ClrType is not null && string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
}


/// <summary>Non-generic view of one entity, for callers that only know the entity name at run time.</summary>
public interface IFoundgineEntity
{
    string Name { get; }
    Type ClrType { get; }
    IReadOnlyList<string> Fields { get; }

    /// <summary>Semantic read (authorized plan -> EF). <paramref name="equals"/> are field = value filters.</summary>
    Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ReadAsync(
        IReadOnlyDictionary<string, object?>? equals = null, IReadOnlyList<string>? fields = null,
        CancellationToken ct = default);

    Task<object?> GetAsync(object id, CancellationToken ct = default);

    /// <summary>Creates a row from a JSON body (field names, case-insensitive). Returns the stored entity.</summary>
    Task<object> CreateAsync(JsonElement body, CancellationToken ct = default);

    Task<int> UpdateAsync(object id, IReadOnlyDictionary<string, object?> values, CancellationToken ct = default);
    Task<int> DeleteAsync(object id, CancellationToken ct = default);
}

/// <summary>Generic implementation: repository for writes, semantic reader for reads, metadata for validation.</summary>
public sealed class FoundgineEntity<T> : IFoundgineEntity where T : class
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly EntityMetadata _meta;
    private readonly IFoundgineRepository<T> _repository;
    private readonly FoundgineEfReader _reader;
    private readonly FieldMetadata? _key;

    public FoundgineEntity(EntityMetadata meta, IFoundgineRepository<T> repository, FoundgineEfReader reader)
    {
        _meta = meta;
        _repository = repository;
        _reader = reader;
        _key = meta.PrimaryKey is { } pk
            ? meta.EffectiveFields.FirstOrDefault(f => f.Column is { } c && c.ColumnId.Equals(pk.ColumnId))
            : null;
    }

    public string Name => _meta.Name;
    public Type ClrType => typeof(T);
    public IReadOnlyList<string> Fields => _meta.EffectiveFields.Select(f => f.Name).ToList();

    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ReadAsync(
        IReadOnlyDictionary<string, object?>? equals = null, IReadOnlyList<string>? fields = null,
        CancellationToken ct = default)
    {
        var selected = (fields is { Count: > 0 } ? fields.Select(Field) : _meta.EffectiveFields).ToList();

        var filters = (equals ?? new Dictionary<string, object?>())
            .Select(kv =>
            {
                var field = Field(kv.Key);
                return (SemanticFilterExpression)new SemanticFieldFilter(
                    field.Id, SemanticFilterOperator.Eq, Coerce(kv.Value, field.ClrType));
            })
            .ToList();
        SemanticFilterExpression? filter = filters.Count switch
        {
            0 => null,
            1 => filters[0],
            _ => new SemanticAndFilter(filters)
        };

        IReadOnlyList<SemanticOrderTerm> order =
            _key is null ? [] : [new SemanticOrderTerm(_key.Id, SemanticSortDirection.Asc)];

        var result = await _reader.ReadAsync(
            _meta.EntityId, selected.Select(f => f.Id).ToList(), filter, order, ct);
        return result.Rows
            .Select(row => (IReadOnlyDictionary<string, object?>)selected.ToDictionary(
                f => f.Name, f => row.Values.TryGetValue(f.Name, out var v) ? v : null))
            .ToList();
    }

    public async Task<object?> GetAsync(object id, CancellationToken ct = default) =>
        await _repository.GetAsync(KeyValue(id), ct);

    public async Task<object> CreateAsync(JsonElement body, CancellationToken ct = default)
    {
        if (body.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("The body must be a JSON object.", nameof(body));
        foreach (var property in body.EnumerateObject()) Field(property.Name); // unknown fields are rejected
        var entity = body.Deserialize<T>(Json)
                     ?? throw new ArgumentException("The body does not describe an entity.", nameof(body));
        return await _repository.AddAsync(entity, ct);
    }

    public Task<int> UpdateAsync(object id, IReadOnlyDictionary<string, object?> values,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(values);
        var typed = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (name, value) in values)
        {
            var field = Field(name);
            if (_key is not null && field.Id.Equals(_key.Id))
                throw new ArgumentException($"The key field '{field.Name}' cannot be updated.", nameof(values));
            typed[field.Name] = Coerce(value, field.ClrType);
        }

        return _repository.UpdateAsync(KeyValue(id), typed, ct);
    }

    public Task<int> DeleteAsync(object id, CancellationToken ct = default) =>
        _repository.DeleteAsync(KeyValue(id), ct);

    private FieldMetadata Field(string name) =>
        _meta.EffectiveFields.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase))
        ?? throw new ArgumentException($"Entity '{_meta.Name}' has no field '{name}'.");

    private object KeyValue(object id) =>
        (_key is null ? id : Coerce(id, _key.ClrType)) ?? throw new ArgumentException("A key is required.");

    /// <summary>Converts JSON values and loose CLR values (long for int, string for number) to the field type.</summary>
    private static object? Coerce(object? value, Type target)
    {
        if (value is null) return null;
        if (value is JsonElement { ValueKind: JsonValueKind.Null }) return null;
        if (value is JsonElement element) return element.Deserialize(target, Json);
        if (target.IsInstanceOfType(value)) return value;

        var underlying = Nullable.GetUnderlyingType(target) ?? target;
        try
        {
            if (value is IConvertible && typeof(IConvertible).IsAssignableFrom(underlying) && !underlying.IsEnum)
                return Convert.ChangeType(value, underlying, CultureInfo.InvariantCulture);
            return JsonSerializer.Deserialize(JsonSerializer.Serialize(value, Json), underlying, Json);
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or JsonException or OverflowException)
        {
            throw new ArgumentException($"Value '{value}' is not a valid {underlying.Name}.");
        }
    }
}
