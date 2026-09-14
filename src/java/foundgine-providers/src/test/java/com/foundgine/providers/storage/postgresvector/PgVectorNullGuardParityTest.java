package com.foundgine.providers.storage.postgresvector;

import org.junit.jupiter.api.Test;

import javax.sql.DataSource;

import static org.junit.jupiter.api.Assertions.*;

/**
 * Port of the remaining C# {@code Foundgine.Postgres.Vector.Tests} cases that
 * {@code PgVectorParityTest} doesn't already cover:
 * {@code PgVectorSemanticLexicalCandidateSourceTests.Retrieve_throws_when_request_is_null},
 * {@code PgVectorSemanticLexiconIndexClientTests.IndexContractAsync_throws_when_contract_is_null},
 * and {@code PgVectorSemanticLexiconIndexClientTests.IndexEntryAsync_throws_when_entry_is_null}.
 *
 * <p>
 * The C# tests await these as {@code Task}s because the C# methods are async;
 * the Java ports ({@code retrieve}, {@code indexContract}, {@code indexEntry})
 * are synchronous, so the null guard is asserted directly rather than through
 * an async wrapper.
 *
 * <p>
 * NOT PORTED — {@code DistanceOperator_rejects_an_undefined_distance_value},
 * {@code ToScore_rejects_an_undefined_distance_value}, and
 * {@code VectorOpsClass_rejects_an_undefined_distance_value}: these construct
 * an out-of-range enum value via an int cast, e.g. {@code (PgVectorDistance)99},
 * which C#'s enums (backed by plain integers) allow. {@code PgVectorDistance}
 * is a real Java {@code enum}, and {@code DistanceOperator}/{@code ToScore}/
 * {@code VectorOpsClass} are exhaustive {@code switch} expressions over it —
 * there is no way to construct an "undefined" {@code PgVectorDistance} through
 * normal code, so there's no reachable path to a guard clause to test.
 */
class PgVectorNullGuardParityTest {

	private static final PgVectorSemanticLexicalCandidateSource.EmbeddingGenerator GENERATOR = token -> new float[] {
			1f };

	@Test
	void retrieveThrowsWhenRequestIsNull() {
		var source = new PgVectorSemanticLexicalCandidateSource(new DummyDataSource(), GENERATOR);

		assertThrows(NullPointerException.class, () -> source.retrieve(null));
	}

	@Test
	void indexContractThrowsWhenContractIsNull() {
		var client = new PgVectorSemanticLexiconIndexClient(new DummyDataSource(), GENERATOR);

		assertThrows(NullPointerException.class, () -> client.indexContract(null));
	}

	@Test
	void indexEntryThrowsWhenEntryIsNull() {
		var client = new PgVectorSemanticLexiconIndexClient(new DummyDataSource(), GENERATOR);

		assertThrows(NullPointerException.class, () -> client.indexEntry(null));
	}

	/** Minimal no-op DataSource — construction never opens a real connection. */
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
