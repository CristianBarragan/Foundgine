package com.foundgine.runtime.controlplane;

import com.foundgine.core.execution.CancellationToken;

import java.util.concurrent.CompletionStage;

/**
 * Port of
 * {@code Foundgine.Runtime.ControlPlane.IAuthorizationRecoverySequenceAnchor}.
 *
 * <p>
 * Durable monotonic anchor for recovery checkpoints. In production this must be
 * backed by storage whose rollback/fork guarantees are stronger than the
 * PostgreSQL database being protected (for example a managed KMS/HSM,
 * append-only ledger, or independent control plane).
 *
 * <p>
 * <b>Porting decision:</b> {@code ValueTask<T>} is ported as
 * {@code CompletionStage<T>} and {@code CancellationToken} as
 * {@link CancellationToken}, matching the convention already used by
 * {@code IExecutionProvider}. Default overloads supply
 * {@link CancellationToken#NONE} for C#'s {@code CancellationToken
 * cancellationToken = default}.
 */
public interface IAuthorizationRecoverySequenceAnchor {
	CompletionStage<Long> readAsync(CancellationToken cancellationToken);

	default CompletionStage<Long> readAsync() {
		return readAsync(CancellationToken.NONE);
	}

	CompletionStage<Boolean> advanceAsync(long sequence, CancellationToken cancellationToken);

	default CompletionStage<Boolean> advanceAsync(long sequence) {
		return advanceAsync(sequence, CancellationToken.NONE);
	}
}
