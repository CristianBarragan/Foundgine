package com.foundgine.samples.supplychain.advanced.intent;

import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.semantic.metadata.EntityMetadata;
import com.foundgine.core.semantic.metadata.FieldMetadata;
import com.foundgine.providers.storage.inmemory.InMemoryDataSet;
import com.foundgine.providers.storage.inmemory.InMemoryRow;
import com.foundgine.samples.supplychain.advanced.data.SupplyChainData;
import com.foundgine.samples.supplychain.advanced.semantics.SupplyChainSemanticModel;

import java.lang.reflect.Modifier;
import java.util.LinkedHashMap;
import java.util.List;

/**
 * Loads the {@link SupplyChainData} fixture into Foundgine's in-memory provider, keyed by the
 * semantic metadata's entity and field identities.
 *
 * <p>Domain records are matched to metadata by name: record type {@code Supplier} to entity
 * {@code Supplier}, component {@code riskScore} to field {@code RiskScore} (case-insensitive).
 * Records with no metadata entity (the command-side {@code Order}, {@code OrderItem},
 * idempotency records, ...) are skipped, and so are components with no metadata field. Enums are
 * stored as their name, as the physical schema does.
 */
final class SupplyChainRows {
    private SupplyChainRows() {}

    static InMemoryDataSet load(SupplyChainData data) {
        var entitiesByName = new LinkedHashMap<String, EntityMetadata>();
        for (var entity : SupplyChainSemanticModel.Metadata.entities()) {
            entitiesByName.put(entity.name().toLowerCase(java.util.Locale.ROOT), entity);
        }
        var set = new InMemoryDataSet();
        try {
            for (var holder : SupplyChainData.class.getFields()) {
                if (Modifier.isStatic(holder.getModifiers()) || !List.class.isAssignableFrom(holder.getType())) {
                    continue;
                }
                for (Object record : (List<?>) holder.get(data)) {
                    var entity = entitiesByName.get(record.getClass().getSimpleName().toLowerCase(java.util.Locale.ROOT));
                    if (entity != null) set.add(row(entity, record));
                }
            }
        } catch (ReflectiveOperationException e) {
            throw new IllegalStateException("Unable to read the supply chain fixture.", e);
        }
        return set;
    }

    private static InMemoryRow row(EntityMetadata entity, Object record) throws ReflectiveOperationException {
        var values = new LinkedHashMap<FieldId, Object>();
        for (var component : record.getClass().getRecordComponents()) {
            FieldMetadata field = null;
            for (var candidate : entity.effectiveFields()) {
                if (candidate.name().equalsIgnoreCase(component.getName())) field = candidate;
            }
            if (field == null) continue;
            Object value = component.getAccessor().invoke(record);
            values.put(field.id(), value instanceof Enum<?> e ? e.name() : value);
        }
        return new InMemoryRow(entity.entityId(), values);
    }
}
