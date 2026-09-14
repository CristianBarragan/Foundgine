package com.foundgine.core.execution;

import java.util.Collection;

/** Provider declaration of security invariants preserved by its compiler. */
public interface ISecurityInvariantProviderCompiler {
	Collection<String> preservedSecurityInvariants();
}
