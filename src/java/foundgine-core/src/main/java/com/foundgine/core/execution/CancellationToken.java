package com.foundgine.core.execution;

import java.util.concurrent.CancellationException;
import java.util.concurrent.CopyOnWriteArrayList;
import java.util.concurrent.atomic.AtomicBoolean;

/**
 * Java analogue of {@code System.Threading.CancellationToken}.
 *
 * <p>
 * Cancellation is observable both by polling and by registering a callback. The
 * callback form is required by physical providers that need to interrupt a
 * blocking JDBC/HTTP operation rather than waiting for the operation to return.
 */
public final class CancellationToken {

	/** Equivalent to C#'s {@code default(CancellationToken)}: never cancelled. */
	public static final CancellationToken NONE = new CancellationToken(new AtomicBoolean(false));

	private final AtomicBoolean cancelled;
	private final CopyOnWriteArrayList<Runnable> registrations = new CopyOnWriteArrayList<>();

	/**
	 * Creates an uncancelled standalone token for compatibility with direct
	 * callers.
	 */
	public CancellationToken() {
		this(new AtomicBoolean(false));
	}

	CancellationToken(AtomicBoolean cancelled) {
		this.cancelled = cancelled;
	}

	public boolean isCancellationRequested() {
		return cancelled.get();
	}

	public void throwIfCancellationRequested() {
		if (cancelled.get()) {
			throw new CancellationException("Operation was cancelled.");
		}
	}

	/**
	 * Registers a callback which is invoked when this token is cancelled. If
	 * cancellation has already happened, the callback is invoked before the
	 * registration is returned. The returned handle removes the callback.
	 */
	public AutoCloseable register(Runnable callback) {
		if (callback == null)
			throw new NullPointerException("callback");
		if (cancelled.get()) {
			callback.run();
			return () -> {
			};
		}

		registrations.add(callback);
		if (cancelled.get() && registrations.remove(callback)) {
			callback.run();
		}
		return () -> registrations.remove(callback);
	}

	void cancel() {
		if (!cancelled.compareAndSet(false, true))
			return;
		for (Runnable callback : registrations) {
			try {
				callback.run();
			} catch (RuntimeException ignored) {
				// Cancellation callbacks are best-effort and must not prevent
				// the remaining callbacks from receiving cancellation.
			}
		}
		registrations.clear();
	}
}
