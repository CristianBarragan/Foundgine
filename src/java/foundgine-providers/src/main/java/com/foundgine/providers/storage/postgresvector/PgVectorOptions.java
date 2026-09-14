package com.foundgine.providers.storage.postgresvector;

/** Configuration for the pgvector-backed semantic lexicon projection. */
public record PgVectorOptions(String tableName, int dimensions, PgVectorDistance distance, String schema) {
	public PgVectorOptions() {
		this("foundgine_semantic_lexicon", 1536, PgVectorDistance.COSINE, "public");
	}

	public PgVectorOptions {
		if (tableName == null || tableName.isBlank())
			throw new IllegalArgumentException("tableName");
		if (dimensions < 1)
			throw new IllegalArgumentException("dimensions");
		if (distance == null)
			throw new NullPointerException("distance");
		if (schema == null || schema.isBlank())
			throw new IllegalArgumentException("schema");
	}

	public String qualifiedTableName() {
		return "\"" + quote(schema) + "\".\"" + quote(tableName) + "\"";
	}

	private static String quote(String value) {
		return value.replace("\"", "\"\"");
	}
}
