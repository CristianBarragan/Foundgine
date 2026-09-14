package com.foundgine.runtime.api;

import com.foundgine.core.execution.ExecutionResult;
import com.foundgine.core.execution.ExecutionContext;
import com.foundgine.core.semantic.*;
import com.foundgine.core.semantic.capabilities.*;
import com.foundgine.core.semantic.security.execution.SecurityExecutionContext;
import com.foundgine.core.semantic.intent.ReadIntent;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.CancellationException;

public interface IFoundgineExecutor {
	CompletableFuture<ExecutionResult> executeAsync(SemanticRequest request, ExecutionContext context);

	CompletableFuture<ExecutionResult> executeAsync(ReadIntent intent, ExecutionContext context);

	default CompletableFuture<ExecutionResult> executeAsync(SemanticRequest request) {
		return executeAsync(request, null);
	}

	default CompletableFuture<ExecutionResult> executeAsync(ReadIntent intent) {
		return executeAsync(intent, null);
	}
}
