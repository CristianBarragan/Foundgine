package com.foundgine.core.semantic.resolution;

import java.util.List;

public final class ResolutionResult {
	private final ResolutionOutcome outcome;
	private final ResolvedReference resolved;
	private final String unresolvedReason;
	private final List<ResolutionEvidence> evidence;

	private ResolutionResult(ResolutionOutcome outcome, ResolvedReference resolved, String reason,
			List<ResolutionEvidence> evidence) {
		this.outcome = outcome;
		this.resolved = resolved;
		this.unresolvedReason = reason;
		this.evidence = evidence == null ? List.of() : List.copyOf(evidence);
	}

	public static ResolutionResult success(ResolvedReference reference) {
		return new ResolutionResult(ResolutionOutcome.RESOLVED, reference, null, reference.evidence());
	}

	public static ResolutionResult ambiguous(String reason, List<ResolutionEvidence> evidence) {
		return new ResolutionResult(ResolutionOutcome.AMBIGUOUS, null, reason, evidence);
	}

	public static ResolutionResult notFound(String reason, List<ResolutionEvidence> evidence) {
		return new ResolutionResult(ResolutionOutcome.NOT_FOUND, null, reason, evidence);
	}

	public ResolutionOutcome outcome() {
		return outcome;
	}

	public ResolvedReference resolved() {
		return resolved;
	}

	public String unresolvedReason() {
		return unresolvedReason;
	}

	public List<ResolutionEvidence> evidence() {
		return evidence;
	}
}
