package com.foundgine.providers.storage.sql;

import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.abstractions.FieldId;

public record SqlColumnBinding(String resultName, EntityId entityId, FieldId fieldId, String columnName, int nodeId,
		boolean isCursor) {
	public SqlColumnBinding(String resultName, EntityId entityId, FieldId fieldId, String columnName, int nodeId) {
		this(resultName, entityId, fieldId, columnName, nodeId, false);
	}
}
