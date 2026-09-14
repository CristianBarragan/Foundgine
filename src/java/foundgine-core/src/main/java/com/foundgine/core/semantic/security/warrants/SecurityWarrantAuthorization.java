package com.foundgine.core.semantic.security.warrants;

import java.math.BigDecimal;
import java.time.Instant;

public final class SecurityWarrantAuthorization {
	private SecurityWarrantAuthorization() {
	}

	public static boolean allows(SecurityWarrant warrant, String subject, String audience, String capability,
			String operation, String tenant, String resourceScope, Long requestedResults, BigDecimal requestedAmount,
			boolean requireResourceScopeMatch) {
		if (warrant == null)
			throw new NullPointerException("warrant");
		if (!java.util.Objects.equals(warrant.subject(), subject)
				|| !java.util.Objects.equals(warrant.audience(), audience) || !warrant.isTimeValid(Instant.now()))
			return false;
		boolean grant = warrant.grants().stream()
				.anyMatch(g -> java.util.Objects.equals(g.capability(), capability)
						&& java.util.Objects.equals(g.operation(), operation)
						&& (!requireResourceScopeMatch || g.resourceScopes().isEmpty()
								|| (resourceScope != null && g.resourceScopes().contains(resourceScope))));
		if (!grant)
			return false;
		var c = warrant.constraints();
		if (!c.allowedTenants().isEmpty() && (tenant == null || !c.allowedTenants().contains(tenant)))
			return false;
		if (!c.allowedOperations().isEmpty() && !c.allowedOperations().contains(operation))
			return false;
		if (requireResourceScopeMatch && !c.resourceScopes().isEmpty()
				&& (resourceScope == null || !c.resourceScopes().contains(resourceScope)))
			return false;
		if (requestedResults != null && c.maxResults() != null && requestedResults > c.maxResults())
			return false;
		if (requestedAmount != null && c.maxAmount() != null && requestedAmount.compareTo(c.maxAmount()) > 0)
			return false;
		return true;
	}

	public static boolean allows(SecurityWarrant w, String s, String a, String c, String o, String t, String r) {
		return allows(w, s, a, c, o, t, r, null, null, true);
	}
}
