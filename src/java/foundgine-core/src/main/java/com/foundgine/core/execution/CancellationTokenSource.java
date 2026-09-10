package com.foundgine.core.execution;

import java.time.Duration;
import java.util.concurrent.Executors;
import java.util.concurrent.ScheduledExecutorService;
import java.util.concurrent.ScheduledFuture;
import java.util.concurrent.ThreadFactory;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicBoolean;

/**
 * Placeholder port of {@code System.Threading.CancellationTokenSource}.
 *
 * <p>Supports what {@link com.foundgine.core.execution.ExecutionContext#createDeadlineCancellationSource}
 * needs: {@link #cancel()}, {@link #cancelAfter(Duration)}, and
 * {@link #createLinkedTokenSource(CancellationToken)}. As with
 * {@link CancellationToken}, this is a placeholder: because {@code CancellationToken}
 * has no callback-registration mechanism yet, a linked source does not
 * observe a cancellation requested on the caller token *after* the link was
 * created — only a caller token already cancelled at link time is honored.
 * Revisit together with {@link CancellationToken} if live propagation is
 * needed.
 */
public final class CancellationTokenSource implements AutoCloseable {

    private static final ScheduledExecutorService SCHEDULER = Executors.newSingleThreadScheduledExecutor(daemonThreadFactory());

    private final AtomicBoolean cancelled = new AtomicBoolean(false);
    private final CancellationToken token = new CancellationToken(cancelled);
    private volatile ScheduledFuture<?> pendingCancel;

    public CancellationToken token() {
        return token;
    }

    public boolean isCancellationRequested() {
        return cancelled.get();
    }

    public void cancel() {
        cancelled.set(true);
        cancelPending();
    }

    public void cancelAfter(Duration delay) {
        if (delay == null) {
            throw new NullPointerException("delay");
        }
        cancelPending();
        if (delay.isZero() || delay.isNegative()) {
            cancel();
            return;
        }
        pendingCancel = SCHEDULER.schedule(this::cancel, delay.toMillis(), TimeUnit.MILLISECONDS);
    }

    /** Port of {@code CancellationTokenSource.CreateLinkedTokenSource(CancellationToken)}. See class caveat above. */
    public static CancellationTokenSource createLinkedTokenSource(CancellationToken callerToken) {
        CancellationTokenSource source = new CancellationTokenSource();
        if (callerToken != null && callerToken.isCancellationRequested()) {
            source.cancel();
        }
        return source;
    }

    private void cancelPending() {
        ScheduledFuture<?> future = pendingCancel;
        if (future != null) {
            future.cancel(false);
        }
    }

    @Override
    public void close() {
        cancelPending();
    }

    private static ThreadFactory daemonThreadFactory() {
        return runnable -> {
            Thread thread = new Thread(runnable, "foundgine-cancellation-scheduler");
            thread.setDaemon(true);
            return thread;
        };
    }
}
