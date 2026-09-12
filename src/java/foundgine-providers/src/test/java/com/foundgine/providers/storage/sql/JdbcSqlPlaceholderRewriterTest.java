package com.foundgine.providers.storage.sql;

import static org.junit.jupiter.api.Assertions.*;
import org.junit.jupiter.api.Test;

final class JdbcSqlPlaceholderRewriterTest {
	@Test
	void rewrites_foundgine_named_placeholders_to_jdbc_positional_placeholders() {
		assertEquals("SELECT * FROM t WHERE a = ? AND b = ? LIMIT ? OFFSET ? AND c = ?",
				JdbcSqlPlaceholderRewriter.rewrite(
						"SELECT * FROM t WHERE a = @p0 AND b = @auth0 LIMIT @__fg_limit OFFSET @__fg_offset AND c = @p12"));
	}

	@Test
	void leaves_sql_literals_and_identifiers_untouched() {
		assertEquals("SELECT '@p0', \"@p1\" FROM t WHERE a = ?",
				JdbcSqlPlaceholderRewriter.rewrite("SELECT '@p0', \"@p1\" FROM t WHERE a = @p0"));
	}

	@Test
	void leaves_unknown_at_tokens_untouched() {
		assertEquals("SELECT email @> '{\"x\":1}' FROM t WHERE a = ?",
				JdbcSqlPlaceholderRewriter.rewrite("SELECT email @> '{\"x\":1}' FROM t WHERE a = @p0"));
	}
}
