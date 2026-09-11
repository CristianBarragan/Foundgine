package com.foundgine.providers.storage.sql;
import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.semantic.query.SemanticSortDirection;
public record SqlCursorBinding(String resultName, EntityId entityId, FieldId fieldId, Class<?> clrType, SemanticSortDirection direction) {}
