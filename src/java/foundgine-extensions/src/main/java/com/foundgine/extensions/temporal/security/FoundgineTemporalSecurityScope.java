package com.foundgine.extensions.temporal.security;

import com.foundgine.core.semantic.security.execution.ISecurityExecutionContextProvider;
import com.foundgine.core.semantic.security.execution.SecurityExecutionContext;
import java.util.Objects;
import java.util.function.Supplier;

/**
 * Per-activity holder exposed as {@link ISecurityExecutionContextProvider} so Foundgine services used inside an
 * activity see the activity's context, not an HTTP one. It is bound to the thread running the activity.
 *
 * <p>The runtime consumes a warrant nonce once, so an activity that makes several Foundgine calls needs a fresh warrant
 * for each: the first call gets the context created up front, later calls get one minted by the same factory.
 */
public final class FoundgineTemporalSecurityScope implements ISecurityExecutionContextProvider {
    private static final class State {
        SecurityExecutionContext first;
        Supplier<SecurityExecutionContext> renew;
    }

    private final ThreadLocal<State> state = new ThreadLocal<>();

    @Override
    public SecurityExecutionContext getSecurityExecutionContext() {
        var s = state.get();
        if (s == null) return null;
        if (s.first != null) {
            var first = s.first;
            s.first = null;
            return first;
        }
        return s.renew == null ? null : s.renew.get();
    }

    /** Binds a context to the current thread; pair with {@link #clear()} in a finally block. */
    public void set(SecurityExecutionContext context, Supplier<SecurityExecutionContext> renew) {
        Objects.requireNonNull(context, "context");
        var s = new State();
        s.first = context;
        s.renew = renew;
        state.set(s);
    }

    public void clear() { state.remove(); }
}
