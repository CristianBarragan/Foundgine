package com.foundgine.providers.storage.sql.mutation;

import org.junit.jupiter.api.Test;

import static org.junit.jupiter.api.Assertions.assertEquals;

class SqlMutationExecutionProviderTest {
	@Test
	void jdbcNormalizesFoundgineNamedParametersToPositionalPlaceholders() {
		assertEquals("UPDATE \"orders\" SET \"quantity\" = ? WHERE \"id\" = ? AND \"tenant_id\" = ?",
				SqlMutationExecutionProvider.toJdbcSql(
						"UPDATE \"orders\" SET \"quantity\" = @p0 WHERE \"id\" = @p1 AND \"tenant_id\" = @p12"));
	}

	@Test
	void jdbcLeavesAtSignsThatAreNotFoundgineParametersUntouched() {
		assertEquals("SELECT '@p', email FROM users WHERE email LIKE '%@example.com'", SqlMutationExecutionProvider
				.toJdbcSql("SELECT '@p', email FROM users WHERE email LIKE '%@example.com'"));
	}
}
