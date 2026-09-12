package com.foundgine.providers.storage.inmemory;

import com.foundgine.core.execution.CancellationToken;
import com.foundgine.core.execution.ExecutionContext;
import com.foundgine.core.execution.ExecutionResult;
import com.foundgine.core.execution.IExecutionProvider;
import com.foundgine.core.execution.ProviderPlan;

import com.foundgine.core.semantic.metadata.IMetadataProvider;

import java.util.Objects;
import java.util.concurrent.CompletionStage;

/**
 * Executes a provider-neutral ExecutionIR directly against an InMemoryDataSet.
 * No SQL representation or SQL parser is involved.
 */
public final class InMemoryExecutionProvider implements IExecutionProvider {
	private final InMemoryCompiler compiler;

	public InMemoryExecutionProvider(IMetadataProvider metadata, InMemoryDataSet data) {
		this.compiler = new InMemoryCompiler(metadata, data);
	}

	@Override
	public CompletionStage<ExecutionResult> executeAsync(ProviderPlan plan, ExecutionContext context,
			CancellationToken cancellationToken) {
		return compiler.executeAsync(plan, context, cancellationToken);
	}
}
