package com.foundgine.core.semantic.security.warrants;

import java.nio.charset.StandardCharsets;
import java.security.*;
import java.time.*;
import java.util.*;

public final class SecurityWarrantCanonicalizer {
	private SecurityWarrantCanonicalizer() {
	}

	public static byte[] unsignedBytes(SecurityWarrant w) {
		return unsignedJson(w).getBytes(StandardCharsets.UTF_8);
	}

	public static String unsignedJson(SecurityWarrant w) {
		Objects.requireNonNull(w, "warrant");
		var grants = new ArrayList<>(w.grants());
		grants.sort(Comparator.comparing(CapabilityGrant::capability).thenComparing(CapabilityGrant::operation)
				.thenComparing(g -> String.join("\u001f", g.resourceScopes())));
		var s = new StringBuilder("{");
		key(s, "id", true);
		str(s, w.id());
		key(s, "issuer", false);
		str(s, w.issuer());
		key(s, "subject", false);
		str(s, w.subject());
		key(s, "audience", false);
		str(s, w.audience());
		key(s, "grants", false);
		s.append('[');
		for (int i = 0; i < grants.size(); i++) {
			if (i > 0)
				s.append(',');
			var g = grants.get(i);
			s.append('{');
			key(s, "capability", true);
			str(s, g.capability());
			key(s, "operation", false);
			str(s, g.operation());
			key(s, "resourceScopes", false);
			array(s, g.resourceScopes());
			s.append('}');
		}
		s.append(']');
		var c = w.constraints();
		key(s, "constraints", false);
		s.append('{');
		key(s, "allowedTenants", true);
		array(s, c.allowedTenants());
		key(s, "allowedFields", false);
		array(s, c.allowedFields());
		key(s, "resourceScopes", false);
		array(s, c.resourceScopes());
		key(s, "allowedOperations", false);
		array(s, c.allowedOperations());
		key(s, "maxResults", false);
		s.append(c.maxResults() == null ? "null" : c.maxResults());
		key(s, "maxAmount", false);
		s.append(c.maxAmount() == null ? "null" : c.maxAmount().toPlainString());
		s.append('}');
		key(s, "issuedAt", false);
		str(s, format(w.issuedAt()));
		key(s, "expiresAt", false);
		str(s, format(w.expiresAt()));
		key(s, "nonce", false);
		str(s, w.nonce());
		key(s, "keyId", false);
		str(s, w.keyId());
		key(s, "parentId", false);
		if (w.parentId() == null)
			s.append("null");
		else
			str(s, w.parentId());
		key(s, "parentDigest", false);
		if (w.parentDigest() == null)
			s.append("null");
		else
			str(s, w.parentDigest());
		key(s, "delegationPath", false);
		array(s, w.delegationPath());
		s.append('}');
		return s.toString();
	}

	public static String digest(SecurityWarrant w) {
		try {
			return HexFormat.of().withUpperCase()
					.formatHex(MessageDigest.getInstance("SHA-256").digest(unsignedBytes(w)));
		} catch (NoSuchAlgorithmException e) {
			throw new IllegalStateException(e);
		}
	}

	private static void key(StringBuilder s, String k, boolean first) {
		if (!first)
			s.append(',');
		str(s, k);
		s.append(':');
	}

	private static void array(StringBuilder s, List<String> v) {
		s.append('[');
		for (int i = 0; i < v.size(); i++) {
			if (i > 0)
				s.append(',');
			str(s, v.get(i));
		}
		s.append(']');
	}

	private static void str(StringBuilder s, String v) {
		s.append('"');
		if (v == null)
			throw new NullPointerException("canonical string");
		for (int i = 0; i < v.length(); i++) {
			char c = v.charAt(i);
			switch (c) {
			case '"' -> s.append("\\\"");
			case '\\' -> s.append("\\\\");
			case '\n' -> s.append("\\n");
			case '\r' -> s.append("\\r");
			case '\t' -> s.append("\\t");
			case '\b' -> s.append("\\b");
			case '\f' -> s.append("\\f");
			default -> {
				if (c < 0x20)
					s.append(String.format("\\u%04x", (int) c));
				else
					s.append(c);
			}
			}
		}
		s.append('"');
	}

	private static String format(Instant i) {
		var u = i.atOffset(ZoneOffset.UTC);
		int n = i.getNano();
		return String.format("%04d-%02d-%02dT%02d:%02d:%02d.%07dZ", u.getYear(), u.getMonthValue(), u.getDayOfMonth(),
				u.getHour(), u.getMinute(), u.getSecond(), n / 100);
	}
}
