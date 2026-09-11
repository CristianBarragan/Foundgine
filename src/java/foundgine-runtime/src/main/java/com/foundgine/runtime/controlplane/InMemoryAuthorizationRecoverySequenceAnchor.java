package com.foundgine.runtime.controlplane;

import com.foundgine.core.execution.CancellationToken;

import java.util.concurrent.CompletableFuture;
import java.util.concurrent.CompletionStage;
import java.util.concurrent.atomic.AtomicLong;

/**
 * Port of {@code Foundgine.Runtime.ControlPlane.InMemoryAuthorizationRecoverySequenceAnchor}.
 *
 * <p>Test/reference anchor. Production deployments must replace this with
 * independent durable storage.
 *
 * <p><b>Porting decision:</b> C#'s {@code Interlocked.Read} /
 * {@code Interlocked.CompareExchange} over a {@code long} field are ported
 * as {@link AtomicLong#get()} and {@link AtomicLong#compareAndSet}, the
 * direct Java equivalents.
 */
public final class InMemoryAuthorizationRecoverySequenceAnchor implements IAuthorizationRecoverySequenceAnchor {
    private final AtomicLong sequence = new AtomicLong();

    @Override
    public CompletionStage<Long> readAsync(CancellationToken cancellationToken) {
        return CompletableFuture.completedFuture(sequence.get());
    }

    @Override
    public CompletionStage<Boolean> advanceAsync(long sequence, CancellationToken cancellationToken) {
        while (true) {
            var current = this.sequence.get();
            if (sequence <= current) {
                return CompletableFuture.completedFuture(false);
            }
            if (this.sequence.compareAndSet(current, sequence)) {
                return CompletableFuture.completedFuture(true);
            }
        }
    }
}
