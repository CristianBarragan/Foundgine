package com.foundgine.runtime.capabilities;

import com.foundgine.runtime.FoundgineOptions;

/** Java fluent equivalent of the C# HighAssurance extension methods. */
public final class HighAssuranceFoundgineOptionsExtensions {
	private HighAssuranceFoundgineOptionsExtensions() {
	}

	public static FoundgineOptions useHighAssurance(FoundgineOptions options) {
		return options.enable(HighAssurance.class, new HighAssurance());
	}

	public static FoundgineOptions disableHighAssurance(FoundgineOptions options) {
		return options.disable(HighAssurance.class);
	}
}
