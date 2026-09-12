package com.foundgine.core.semantic.security.warrants;

import java.nio.ByteBuffer;
import java.security.MessageDigest;
import java.time.Instant;
import java.util.HashSet;
import java.util.List;
import java.util.Set;

public final class SecurityWarrantDelegationChainValidator {
	public static final int MAX_DELEGATION_DEPTH = SecurityWarrantAttenuator.MAX_DELEGATION_DEPTH;

	private SecurityWarrantDelegationChainValidator() {
	}

	public static void validate(List<SecurityWarrant> chain, Instant now) {
		validate(chain, now, null);
	}

	public static void validate(List<SecurityWarrant> chain, Instant now, String expectedRootDigest) {
		if (chain == null)
			throw new NullPointerException("chain");
		if (chain.isEmpty())
			throw new IllegalStateException("A delegation chain must contain at least one warrant.");
		if (chain.size() > MAX_DELEGATION_DEPTH + 1)
			throw new IllegalStateException("Delegation chain exceeds the maximum supported depth.");
		SecurityWarrant root = chain.get(0);
		if (root.parentId() != null || root.parentDigest() != null || !root.delegationPath().isEmpty())
			throw new IllegalStateException("The root warrant cannot contain delegation ancestry.");
		if (!root.isTimeValid(now))
			throw new IllegalStateException("The root warrant is not currently valid.");
		if (expectedRootDigest != null && !root.digest().equals(expectedRootDigest))
			throw new IllegalStateException("Delegation chain root does not match the expected root digest.");
		Set<String> ids = new HashSet<>(), digests = new HashSet<>();
		addUnique(root, ids, digests);
		for (int i = 1; i < chain.size(); i++) {
			SecurityWarrant parent = chain.get(i - 1), child = chain.get(i);
			if (!parent.isTimeValid(now))
				throw new IllegalStateException("A delegation ancestor is no longer currently valid.");
			if (!child.isTimeValid(now))
				throw new IllegalStateException("A delegated warrant is not currently valid.");
			if (!java.util.Objects.equals(child.parentId(), parent.id()))
				throw new IllegalStateException("Delegation chain contains a parent-id substitution or reorder.");
			if (!java.util.Objects.equals(child.parentDigest(), parent.digest()))
				throw new IllegalStateException("Delegation chain contains a parent-digest substitution.");
			if (!java.util.Objects.equals(child.issuer(), parent.subject()))
				throw new IllegalStateException("Delegation chain issuer does not match the parent subject.");
			if (child.delegationDepth() != i || child.delegationPath().size() != i)
				throw new IllegalStateException(
						"Delegation depth/path length does not match the supplied chain position.");
			for (int p = 0; p < i; p++)
				if (!java.util.Objects.equals(child.delegationPath().get(p), chain.get(p).digest()))
					throw new IllegalStateException(
							"Delegation path contains a splice, reorder, or substituted ancestor.");
			if (new HashSet<>(child.delegationPath()).size() != child.delegationPath().size())
				throw new IllegalStateException("Delegation chain contains a repeated ancestor.");
			addUnique(child, ids, digests);
		}
	}

	public static String chainDigest(List<SecurityWarrant> chain) {
		validateShape(chain);
		try {
			MessageDigest sha = MessageDigest.getInstance("SHA-256");
			for (SecurityWarrant warrant : chain) {
				byte[] bytes = hex(warrant.digest());
				sha.update(ByteBuffer.allocate(4).putInt(bytes.length).array());
				sha.update(bytes);
			}
			return java.util.HexFormat.of().withUpperCase().formatHex(sha.digest());
		} catch (Exception e) {
			throw new IllegalStateException("Unable to digest delegation chain.", e);
		}
	}

	private static void validateShape(List<SecurityWarrant> chain) {
		if (chain == null)
			throw new NullPointerException("chain");
		if (chain.isEmpty())
			throw new IllegalStateException("A delegation chain must contain at least one warrant.");
		if (chain.size() > MAX_DELEGATION_DEPTH + 1)
			throw new IllegalStateException("Delegation chain exceeds the maximum supported depth.");
	}

	private static byte[] hex(String s) {
		return java.util.HexFormat.of().parseHex(s);
	}

	private static void addUnique(SecurityWarrant w, Set<String> ids, Set<String> digests) {
		if (!ids.add(w.id()))
			throw new IllegalStateException("Delegation chain contains a repeated warrant id.");
		if (!digests.add(w.digest()))
			throw new IllegalStateException("Delegation chain contains a repeated warrant digest.");
	}
}
