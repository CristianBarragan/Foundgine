package com.foundgine.core.serialization;

/**
 * Bounds applied while parsing untrusted structured intent. These limits are
 * intentionally enforced at the protocol boundary before semantic resolution.
 *
 * <p>
 * Ported per this codebase's established pattern for a C# {@code init}-only
 * record with all-defaulted properties: the full canonical constructor plus a
 * no-arg convenience constructor carrying the same defaults as the C# original.
 */
public record JsonReadIntentAdapterOptions(int maxSelectionDepth, int maxSelections, int maxFilterDepth,
		int maxFilterNodes, int maxJsonValueDepth, boolean rejectUnknownProperties) {

	public JsonReadIntentAdapterOptions() {
		this(32, 256, 32, 256, 16, true);
	}
}
