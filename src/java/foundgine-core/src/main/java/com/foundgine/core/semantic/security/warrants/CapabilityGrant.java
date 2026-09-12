package com.foundgine.core.semantic.security.warrants;

import java.util.*;

public record CapabilityGrant(String capability, String operation, List<String> resourceScopes) {
	public CapabilityGrant(String capability, String operation, Collection<String> scopes) {
		this(require(capability, "capability"), require(operation, "operation"), normalize(scopes));
	}

	public CapabilityGrant(String capability, String operation) {
		this(capability, operation, (Collection<String>) null);
	}

	private static String require(String v, String n) {
		if (v == null || v.isBlank())
			throw new IllegalArgumentException("Value is required: " + n);
		return v;
	}

	private static List<String> normalize(Collection<String> v) {
		if (v == null)
			return List.of();
		return v.stream().filter(x -> x != null && !x.isBlank()).distinct().sorted().toList();
	}
}
