package com.foundgine.providers.storage.postgresvector;

import static org.junit.jupiter.api.Assertions.*;
import java.util.*;
import javax.sql.DataSource;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.CsvSource;
import org.junit.jupiter.params.provider.EnumSource;

final class PgVectorParityTest {
	@Test
	void defaults_match_documented_lexicon_shape() {
		var o = new PgVectorOptions();
		assertEquals("foundgine_semantic_lexicon", o.tableName());
		assertEquals(1536, o.dimensions());
		assertEquals(PgVectorDistance.COSINE, o.distance());
		assertEquals("public", o.schema());
		assertEquals("\"public\".\"foundgine_semantic_lexicon\"", o.qualifiedTableName());
	}

	@Test
	void qualified_table_name_quotes_custom_schema_and_table() {
		var o = new PgVectorOptions("lexicon", 1536, PgVectorDistance.COSINE, "fg_vector");
		assertEquals("\"fg_vector\".\"lexicon\"", o.qualifiedTableName());
	}

	@Test
	void qualified_table_name_escapes_embedded_quotes() {
		var o = new PgVectorOptions("lex\"icon", 1536, PgVectorDistance.COSINE, "fg\"vector");
		assertEquals("\"fg\"\"vector\".\"lex\"\"icon\"", o.qualifiedTableName());
	}

	@ParameterizedTest
	@CsvSource({ "COSINE, <=>", "L2, <->", "INNER_PRODUCT, <#>" })
	void distance_operator_matches_pgvector(PgVectorDistance distance, String expected) {
		assertEquals(expected, PgVectorSemanticLexicalCandidateSource.DistanceOperator(distance));
	}

	@ParameterizedTest
	@CsvSource({ "COSINE, 0.0, 1.0", "COSINE, 1.0, 0.0", "COSINE, 0.25, 0.75", "L2, 0.0, 1.0", "L2, 1.0, 0.5",
			"L2, 3.0, 0.25", "INNER_PRODUCT, -0.9, 0.9", "INNER_PRODUCT, 0.4, -0.4" })
	void distance_is_converted_to_provider_score(PgVectorDistance metric, double distance, double expected) {
		assertEquals(expected, PgVectorSemanticLexicalCandidateSource.ToScore(distance, metric), 1e-10);
	}

	@ParameterizedTest
	@EnumSource(PgVectorDistance.class)
	void vector_ops_class_matches_distance(PgVectorDistance distance) {
		String expected = switch (distance) {
		case COSINE -> "vector_cosine_ops";
		case L2 -> "vector_l2_ops";
		case INNER_PRODUCT -> "vector_ip_ops";
		};
		assertEquals(expected, PgVectorSemanticLexiconIndexClient.VectorOpsClass(distance));
	}

	@Test
	void constructors_reject_missing_data_source_or_embedding_generator() {
		var generator = (PgVectorSemanticLexicalCandidateSource.EmbeddingGenerator) token -> new float[] { 1f };
		assertThrows(NullPointerException.class, () -> new PgVectorSemanticLexicalCandidateSource(null, generator));
		assertThrows(NullPointerException.class,
				() -> new PgVectorSemanticLexicalCandidateSource(new DummyDataSource(), null));
		assertThrows(NullPointerException.class, () -> new PgVectorSemanticLexiconIndexClient(null, generator));
		assertThrows(NullPointerException.class,
				() -> new PgVectorSemanticLexiconIndexClient(new DummyDataSource(), null));
	}

	@Test
	void null_options_use_documented_defaults() {
		var generator = (PgVectorSemanticLexicalCandidateSource.EmbeddingGenerator) token -> new float[] { 1f };
		var source = new PgVectorSemanticLexicalCandidateSource(new DummyDataSource(), generator, null);
		var client = new PgVectorSemanticLexiconIndexClient(new DummyDataSource(), generator, null);
		assertNotNull(source);
		assertNotNull(client);
	}

	private static final class DummyDataSource implements DataSource {
		public java.sql.Connection getConnection() {
			throw new UnsupportedOperationException();
		}

		public java.sql.Connection getConnection(String u, String p) {
			throw new UnsupportedOperationException();
		}

		public <T> T unwrap(Class<T> c) {
			throw new UnsupportedOperationException();
		}

		public boolean isWrapperFor(Class<?> c) {
			return false;
		}

		public java.io.PrintWriter getLogWriter() {
			return null;
		}

		public void setLogWriter(java.io.PrintWriter w) {
		}

		public void setLoginTimeout(int s) {
		}

		public int getLoginTimeout() {
			return 0;
		}

		public java.util.logging.Logger getParentLogger() {
			return java.util.logging.Logger.getGlobal();
		}
	}
}
