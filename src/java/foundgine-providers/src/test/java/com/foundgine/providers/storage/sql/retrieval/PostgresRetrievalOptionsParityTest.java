package com.foundgine.providers.storage.sql.retrieval;

import static org.junit.jupiter.api.Assertions.*;
import org.junit.jupiter.api.Test;

final class PostgresRetrievalOptionsParityTest {
	@Test
	void defaults_match_csharp_provider_defaults() {
		var options = new PostgresRetrievalOptions();
		assertTrue(options.enablePgTrgm());
		assertTrue(options.enableFullText());
		assertFalse(options.enablePgSearch());
		assertFalse(options.enableApacheAge());
		assertEquals("english", options.fullTextConfiguration());
		assertEquals("foundgine", options.ageGraphName());
	}

	@Test
	void search_and_graph_similarity_are_explicit_opt_in_capabilities() {
		var options = new PostgresRetrievalOptions(true, true, true, true, "simple", "fg");
		assertTrue(options.enablePgSearch());
		assertTrue(options.enableApacheAge());
		assertEquals("simple", options.fullTextConfiguration());
		assertEquals("fg", options.ageGraphName());
	}
}
