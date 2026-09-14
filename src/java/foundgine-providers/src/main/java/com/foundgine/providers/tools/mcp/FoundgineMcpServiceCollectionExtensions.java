package com.foundgine.providers.tools.mcp;

import com.foundgine.runtime.*;

public final class FoundgineMcpServiceCollectionExtensions {
	private FoundgineMcpServiceCollectionExtensions() {
	}

	public static void register(FoundgineServiceRegistry r, FoundgineMcpTools tools) {
		r.addSingletonInstance(FoundgineMcpTools.class, tools);
	}
}
