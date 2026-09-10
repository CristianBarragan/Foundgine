package com.foundgine.core.semantic.intent;
import com.foundgine.core.semantic.SemanticRequest;
public record SemanticIntentResolution(SemanticIntentDocument document, SemanticRequest request, String contractFingerprint) {}
