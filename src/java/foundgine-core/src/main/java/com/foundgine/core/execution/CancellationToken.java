package com.foundgine.core.execution;

import java.util.concurrent.CancellationException;
import java.util.concurrent.atomic.AtomicBoolean;

/**
 * Placeholder port of {@code System.Threading.CancellationToken}.
 *
 * <p><b>Caveat:</b> .NET's {@code CancellationToken} supports live callback
 * registration ({@code Register}) so that a token observes cancellation
 * requested on a linked source at any point in the future. This port only
 * exposes the poll-based {@link #isCancellationRequested()} /
 * {@link #throwIfCancellationRequested()} surface actually used so far by
 * the ported code ({@code IMutationExecutionProvider}, etc.). Callback
 * registration has not been ported and should be added (e.g. backed by a
 * simple listener list) if/when a caller needs it — flagging here rather
 * than guessing at an API no C# call site has exercised yet.
 *
 * <p>Instances are created via {@link CancellationTokenSource#token()}, or
 * use {@link #NONE} for a token that can never be cancelled (the Java
 * analogue of C#'s {@code default(CancellationToken)}).
 */
public final class CancellationToken {

    /** Equivalent to C#'s {@code default(CancellationToken)}: never cancelled. */
    public static final CancellationToken NONE = new CancellationToken(new AtomicBoolean(false));

    private final AtomicBoolean cancelled;

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
}
