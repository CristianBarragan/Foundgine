package com.foundgine.providers.storage.sql.mutation.postgres;

public record PostgresCorrelationProjection(int groupId, String correlationColumn) {
	public PostgresCorrelationProjection {
		if (groupId < 0)
			throw new IllegalArgumentException("groupId");
	}
}
