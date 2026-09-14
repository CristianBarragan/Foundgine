package com.foundgine.core.abstractions;

import java.nio.charset.StandardCharsets;

/**
 * Port of {@code Foundgine.Core.Abstractions.SemanticIdentity}.
 *
 * <p>
 * Canonical semantic identity and stable hashing rules shared by runtime
 * identifiers and the AOT generator. The canonical key is intentionally
 * independent of JVM/CLR metadata, declaration order, and runtime state, so the
 * 64-bit hash produced here is byte-for-byte identical to the one produced by
 * the original C# implementation for the same input string.
 */
public final class SemanticIdentity {

	public static final String ENTITY_NAMESPACE = "entity";
	public static final String FIELD_NAMESPACE = "field";
	public static final String RELATIONSHIP_NAMESPACE = "relationship";
	public static final String TABLE_NAMESPACE = "table";
	public static final String COLUMN_NAMESPACE = "column";
	public static final String MODEL_NAMESPACE = "model";
	public static final String CONNECTION_NAMESPACE = "connection";
	public static final String AUTHORIZATION_NAMESPACE = "authorization";

	private static final long FNV_OFFSET_BASIS = 0xcbf29ce484222325L; // 14695981039346656037 (unsigned)
	private static final long FNV_PRIME = 0x100000001b3L; // 1099511628211

	private SemanticIdentity() {
	}

	public static String entityKey(String semanticName) {
		return key(ENTITY_NAMESPACE, semanticName);
	}

	public static String fieldKey(String semanticEntityName, String semanticFieldName) {
		return key(FIELD_NAMESPACE, pair(semanticEntityName, semanticFieldName));
	}

	public static String relationshipKey(String semanticEntityName, String semanticRelationshipName) {
		return key(RELATIONSHIP_NAMESPACE, pair(semanticEntityName, semanticRelationshipName));
	}

	public static String tableKey(String storageName) {
		return key(TABLE_NAMESPACE, storageName);
	}

	public static String columnKey(String storageName, String columnName) {
		return key(COLUMN_NAMESPACE, pair(storageName, columnName));
	}

	public static String modelKey(String semanticName) {
		return key(MODEL_NAMESPACE, semanticName);
	}

	public static String connectionKey(String semanticModelName, String semanticConnectionName) {
		return key(CONNECTION_NAMESPACE, pair(semanticModelName, semanticConnectionName));
	}

	public static String authorizationKey(String declaringType, String authorizationName) {
		return key(AUTHORIZATION_NAMESPACE, pair(declaringType, authorizationName));
	}

	/**
	 * Computes the Foundgine stable 64-bit identity hash (FNV-1a over UTF-8 bytes).
	 */
	public static long hash(String canonicalKey) {
		if (canonicalKey == null || canonicalKey.isBlank()) {
			throw new IllegalArgumentException("Canonical identity key is required.");
		}

		long hash = FNV_OFFSET_BASIS;
		for (byte b : canonicalKey.getBytes(StandardCharsets.UTF_8)) {
			hash ^= (b & 0xFFL);
			hash *= FNV_PRIME;
		}

		// Zero is reserved as an invalid/unassigned identity.
		return hash == 0L ? 1L : hash;
	}

	public static String normalize(String value, String parameterName) {
		if (value == null || value.isBlank()) {
			throw new IllegalArgumentException("Identity component is required: " + parameterName);
		}
		return value.trim();
	}

	/** {@code value} is interpreted as an unsigned 64-bit identity. */
	public static long validateExplicitId(long value, String description) {
		if (value == 0L) {
			throw new IllegalArgumentException(
					"Explicit " + description + " identity 0 is reserved and cannot be assigned.");
		}
		return value;
	}

	private static String key(String namespace, String value) {
		return namespace + ":" + normalize(value, "value");
	}

	private static String pair(String left, String right) {
		return normalize(left, "left") + "." + normalize(right, "right");
	}
}
