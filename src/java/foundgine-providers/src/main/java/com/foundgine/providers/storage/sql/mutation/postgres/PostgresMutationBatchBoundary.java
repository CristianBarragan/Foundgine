package com.foundgine.providers.storage.sql.mutation.postgres;
import com.foundgine.core.execution.mutation.MutationBatchResult;
public record PostgresMutationBatchBoundary(String provider,int operationCount,MutationBatchResult result) {}
