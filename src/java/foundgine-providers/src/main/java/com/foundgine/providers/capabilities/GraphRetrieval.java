package com.foundgine.providers.capabilities;

import com.foundgine.runtime.*;

public final class GraphRetrieval implements IFoundgineCapability {
	public void configure(FoundgineCapabilityContext context) {
		context.options().enable(this);
	}

	public static void enable(FoundgineOptions options) {
		options.enable(new GraphRetrieval());
	}
}
