package com.foundgine.runtime.capabilities;

import com.foundgine.runtime.FoundgineOptions;

/** Java fluent equivalent of the C# Grounding extension methods. */
public final class GroundingFoundgineOptionsExtensions {
	private GroundingFoundgineOptionsExtensions() {
	}

	public static FoundgineOptions useGrounding(FoundgineOptions options) {
		return options.enable(Grounding.class, new Grounding());
	}

	public static FoundgineOptions disableGrounding(FoundgineOptions options) {
		return options.disable(Grounding.class);
	}
}
