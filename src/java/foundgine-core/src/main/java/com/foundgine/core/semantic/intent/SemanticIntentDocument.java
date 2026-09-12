package com.foundgine.core.semantic.intent;

import com.foundgine.core.semantic.SemanticRequest;
import java.util.*;

/** Serializable provider-neutral envelope for dynamic semantic intent. */
public record SemanticIntentDocument(String contractFingerprint, ReadIntent intent, int version) {
	public static final int CURRENT_VERSION = 1;

	public SemanticIntentDocument {
		Objects.requireNonNull(contractFingerprint, "contractFingerprint");
		Objects.requireNonNull(intent, "intent");
	}

	public SemanticIntentDocument(String contractFingerprint, ReadIntent intent) {
		this(contractFingerprint, intent, CURRENT_VERSION);
	}

	public SemanticIntentDocument validate() {
		if (version != CURRENT_VERSION)
			throw new IllegalArgumentException("Unsupported semantic intent document version '" + version + "'.");
		if (contractFingerprint.isBlank())
			throw new IllegalArgumentException("Semantic intent document requires a contract fingerprint.");
		return this;
	}
}
