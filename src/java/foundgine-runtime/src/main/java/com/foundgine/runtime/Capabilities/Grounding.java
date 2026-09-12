package com.foundgine.runtime.capabilities;

import com.foundgine.core.semantic.resolution.*;
import com.foundgine.runtime.*;

/**
 * Optional capability wiring the lexical grounding services into a
 * framework-neutral service registry.
 */
public final class Grounding implements IFoundgineCapability {
	@Override
	public void configure(FoundgineCapabilityContext context) {
		var services = context.services();
		services.addSingleton(SemanticLexicalResolver.class,
				() -> new SemanticLexicalResolver(
						services.getRequiredService(com.foundgine.core.semantic.SemanticContractSnapshot.class),
						services.getRequiredService(ISemanticLexicalCandidateSource.class)));
		services.addSingleton(SemanticLexicalReadIntentGrounder.class,
				() -> new SemanticLexicalReadIntentGrounder(
						services.getRequiredService(com.foundgine.core.semantic.SemanticContractSnapshot.class),
						services.getRequiredService(SemanticLexicalResolver.class)));
	}
}
