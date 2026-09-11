package com.foundgine.core.execution;

import java.time.Duration;
import java.util.concurrent.Executors;
import java.util.concurrent.ScheduledExecutorService;
import java.util.concurrent.ScheduledFuture;
import java.util.concurrent.ThreadFactory;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicBoolean;

/**
 * Java analogue of {@code System.Threading.CancellationTokenSource}.
 *
 * <p>Supports cancellation, scheduled cancellation, and live linked-token
 * propagation. Providers can register callbacks on the exposed token to
 * interrupt blocking physical operations.
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
        token.cancel();
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
        if (callerToken != null) {
            source.linkedRegistration = callerToken.register(source::cancel);
        }
        return source;
    }

    private AutoCloseable linkedRegistration;

    private void cancelPending() {
        ScheduledFuture<?> future = pendingCancel;
        if (future != null) {
            future.cancel(false);
        }
    }

    @Override
    public void close() {
        cancelPending();
        AutoCloseable registration = linkedRegistration;
        linkedRegistration = null;
        if (registration != null) {
            try { registration.close(); } catch (Exception ignored) { }
        }
    }

    private static ThreadFactory daemonThreadFactory() {
        return runnable -> {
            Thread thread = new Thread(runnable, "foundgine-cancellation-scheduler");
            thread.setDaemon(true);
            return thread;
        };
    }
}
