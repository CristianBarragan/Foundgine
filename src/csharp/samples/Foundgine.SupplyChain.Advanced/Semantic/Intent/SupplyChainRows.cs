using System.Collections;
using System.Reflection;
using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Metadata;
using Foundgine.Providers.Storage.InMemory;
using Foundgine.SupplyChain.Advanced.Data;
using Foundgine.SupplyChain.Advanced.Semantics;

namespace Foundgine.SupplyChain.Advanced.Intent;

/// <summary>
/// Loads the <see cref="SupplyChainData"/> fixture into Foundgine's in-memory provider, keyed by the
/// semantic metadata's entity and field identities.
/// <para>
/// Domain records are matched to metadata by name: record type <c>Supplier</c> to entity
/// <c>Supplier</c>, property <c>RiskScore</c> to field <c>RiskScore</c> (case-insensitive). Records with
/// no metadata entity are skipped, and so are properties with no metadata field (navigation
/// properties, for one). Strongly typed ids are stored as their integer and enums as their name, as
/// the physical schema does.
/// </para>
/// </summary>
internal static class SupplyChainRows
{
    public static InMemoryDataSet Load(SupplyChainData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var entitiesByName = SupplyChainSemanticModel.Metadata.Entities
            .ToDictionary(entity => entity.Name, StringComparer.OrdinalIgnoreCase);

        var set = new InMemoryDataSet();
        foreach (var holder in typeof(SupplyChainData).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (holder.GetIndexParameters().Length != 0 || holder.GetValue(data) is not IEnumerable records) continue;
            foreach (var record in records)
            {
                if (record is null || !entitiesByName.TryGetValue(record.GetType().Name, out var entity)) continue;
                set.Add(Row(entity, record));
            }
        }

        return set;
    }

    private static InMemoryRow Row(EntityMetadata entity, object record)
    {
        var fieldsByName = entity.EffectiveFields
            .GroupBy(field => field.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var values = new Dictionary<FieldId, object?>();
        foreach (var property in record.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length != 0 || !fieldsByName.TryGetValue(property.Name, out var field))
                continue;
            if (TryStore(property.GetValue(record), out var stored)) values[field.Id] = stored;
        }

        return new InMemoryRow(entity.EntityId, values);
    }

    /// <summary>Stores scalars only; anything else (a navigation property, say) is never copied into a row.</summary>
    private static bool TryStore(object? value, out object? stored)
    {
        stored = null;
        if (value is null) return true;

        var type = value.GetType();
        if (type.IsEnum)
        {
            stored = value.ToString();
            return true;
        }

        // readonly record struct XxxId(int Value)
        if (type.IsValueType && type.Name.EndsWith("Id", StringComparison.Ordinal) &&
            type.GetProperty("Value") is { } inner)
        {
            stored = inner.GetValue(value);
            return true;
        }

        if (type.IsPrimitive || value is string or decimal or DateOnly or DateTime or DateTimeOffset or TimeOnly or Guid)
        {
            stored = value;
            return true;
        }

        return false;
    }
}
