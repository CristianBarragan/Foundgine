package com.foundgine.core.execution;

import java.time.Duration;
import java.time.Instant;
import java.util.Map;
import java.util.Optional;

/**
 * Port of {@code Foundgine.Core.Execution.ExecutionContext} (a C#
 * {@code sealed record}).
 *
 * <p>
 * Runtime values supplied to an already-planned execution. Semantic planning
 * remains independent of these values. A deadline is an execution-time security
 * boundary and is never part of semantic plan shape.
 *
 * <p>
 * The two-argument canonical constructor mirrors the C# record's {@code Values}
 * constructor parameter plus its {@code DeadlineUtc} {@code init}-only
 * property; the one-argument constructor mirrors constructing the C# record
 * without setting {@code DeadlineUtc}.
 */
public record ExecutionContext(Map<String, Object> values, Instant deadlineUtc) {

	/**
	 * Equivalent to {@code new ExecutionContext()} in C# (no values, no deadline).
	 */
	public static final ExecutionContext EMPTY = new ExecutionContext(Map.of(), null);

	public ExecutionContext(Map<String, Object> values) {
		this(values, null);
	}

	public Map<String, Object> effectiveValues() {
		return values != null ? values : Map.of();
	}

	/**
	 * Port of {@code TryGetValue}. Returns an empty {@link Optional} both when the
	 * key is absent and when it is present with a {@code null} value — unlike the
	 * C# {@code out bool} overload, a Java {@code Optional} cannot distinguish the
	 * two. No ported call site needs that distinction today.
	 */
	public Optional<Object> tryGetValue(String path) {
		return Optional.ofNullable(effectiveValues().get(path));
	}

	public void ensureWithinDeadline() {
		ensureWithinDeadline(Instant.now());
	}

	public void ensureWithinDeadline(Instant now) {
		Instant effectiveNow = now != null ? now : Instant.now();
		if (deadlineUtc != null && !effectiveNow.isBefore(deadlineUtc)) {
			throw new IllegalStateException("Execution deadline '" + deadlineUtc + "' has expired.");
		}
	}

	/**
	 * Returns a copy of this context with {@code deadlineUtc} set, mirroring the C#
	 * {@code with { DeadlineUtc = ... }} pattern.
	 */
	public ExecutionContext withDeadline(Instant deadline) {
		return new ExecutionContext(values, deadline);
	}

	public CancellationTokenSource createDeadlineCancellationSource(CancellationToken callerToken) {
		ensureWithinDeadline();
		CancellationTokenSource source = CancellationTokenSource.createLinkedTokenSource(callerToken);
		if (deadlineUtc != null) {
			Duration remaining = Duration.between(Instant.now(), deadlineUtc);
			if (remaining.isZero() || remaining.isNegative()) {
				source.cancel();
			} else {
				source.cancelAfter(remaining);
			}
		}
		return source;
	}
}
