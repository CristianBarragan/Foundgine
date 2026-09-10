package com.foundgine.providers.aot;

import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.abstractions.FieldId;
import com.foundgine.providers.aot.generator.GeneratorSemanticIdentity;
import java.lang.reflect.Field;
import java.util.*;

/** Runtime reflection fallback for the AOT metadata generator contract. */
public final class FoundgineMetadataGenerator {
    private FoundgineMetadataGenerator() {}

    public static List<GeneratedSemanticField> fields(Class<?>... types) {
        Objects.requireNonNull(types);
        List<GeneratedSemanticField> out = new ArrayList<>();
        for (Class<?> type : types) {
            FoundgineEntity entity = type.getAnnotation(FoundgineEntity.class);
            if (entity == null) continue;
            String entityName = entity.name().isBlank() ? type.getSimpleName() : entity.name();
            long entityId = entity.id() != 0 ? GeneratorSemanticIdentity.validateExplicitId(entity.id(), "entity")
                    : GeneratorSemanticIdentity.hash(GeneratorSemanticIdentity.entityKey(entityName));
            EntityId eid = new EntityId(entityId);
            for (Field field : type.getDeclaredFields()) {
                FoundgineField annotation = field.getAnnotation(FoundgineField.class);
                if (annotation == null) continue;
                String fieldName = annotation.name().isBlank() ? field.getName() : annotation.name();
                long fieldId = annotation.id() != 0 ? GeneratorSemanticIdentity.validateExplicitId(annotation.id(), "field")
                        : GeneratorSemanticIdentity.hash(GeneratorSemanticIdentity.fieldKey(entityName, fieldName));
                out.add(new GeneratedSemanticField(eid, new FieldId(fieldId), fieldName));
            }
        }
        return List.copyOf(out);
    }
}
