package com.foundgine.core.semantic.resolution;

import java.util.List;
import java.util.concurrent.CompletableFuture;

public interface ISemanticEmbeddingGenerator {
    CompletableFuture<float[]> embedAsync(String text);
    CompletableFuture<List<float[]>> embedManyAsync(List<String> texts);
}
