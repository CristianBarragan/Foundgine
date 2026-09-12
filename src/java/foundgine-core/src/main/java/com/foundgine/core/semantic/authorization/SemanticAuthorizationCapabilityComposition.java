package com.foundgine.core.semantic.authorization;

import com.foundgine.core.abstractions.*;
import java.util.*;

/** Strict intersection composition: authorization can only become narrower. */
public final class SemanticAuthorizationCapabilityComposition {
	private SemanticAuthorizationCapabilityComposition() {
	}

	public static AuthorizationDecision compose(AuthorizationDecision... decisions) {
		Objects.requireNonNull(decisions);
		var r = AuthorizationDecision.ALLOWED;
		for (var d : decisions)
			r = AuthorizationDecision.combine(r, d);
		return r;
	}

	public static AuthorizationDecision compose(Iterable<AuthorizationDecision> decisions) {
		Objects.requireNonNull(decisions);
		var r = AuthorizationDecision.ALLOWED;
		for (var d : decisions)
			r = AuthorizationDecision.combine(r, d);
		return r;
	}
}
