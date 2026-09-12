package com.foundgine.providers.tools.mcp;

import java.util.concurrent.*;
import java.util.*;

public final class FoundgineMcpMutationTools {
	public interface Executor {
		CompletionStage<Object> execute(String mutationJson);
	}

	private final Executor executor;

	public FoundgineMcpMutationTools(Executor e) {
		executor = Objects.requireNonNull(e);
	}

	public CompletionStage<Object> foundgineMutation(String json) {
		return executor.execute(json);
	}
}
