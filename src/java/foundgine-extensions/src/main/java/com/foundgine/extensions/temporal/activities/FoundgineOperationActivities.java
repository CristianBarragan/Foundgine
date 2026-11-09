package com.foundgine.extensions.temporal.activities;

import com.foundgine.core.execution.CancellationToken;
import com.foundgine.extensions.temporal.contracts.FoundgineOperationRequest;
import com.foundgine.extensions.temporal.contracts.FoundgineOperationResult;
import com.foundgine.extensions.temporal.observability.FoundgineTemporalDiagnostics;
import com.foundgine.extensions.temporal.security.FoundgineTemporalSecurityScope;
import com.foundgine.extensions.temporal.security.IFoundgineTemporalSecurityContextFactory;
import java.util.List;
import java.util.Objects;
import java.util.concurrent.CancellationException;

/** The single activity behind every step: resolves the named operation and runs it under a worker-minted context. */
public final class FoundgineOperationActivities {
    public static final String ACTIVITY_NAME = "foundgine.operation.execute";

    private final List<IFoundgineTemporalOperation> operations;
    private final IFoundgineTemporalSecurityContextFactory securityFactory;
    private final FoundgineTemporalSecurityScope securityScope = new FoundgineTemporalSecurityScope();

    public FoundgineOperationActivities(List<IFoundgineTemporalOperation> operations,
            IFoundgineTemporalSecurityContextFactory securityFactory) {
        this.operations = List.copyOf(Objects.requireNonNull(operations, "operations"));
        this.securityFactory = Objects.requireNonNull(securityFactory, "securityFactory");
    }

    /** The provider Foundgine services inside an operation should use (bound to the activity thread). */
    public FoundgineTemporalSecurityScope securityProvider() { return securityScope; }

    public FoundgineOperationResult execute(FoundgineOperationRequest request, FoundgineActivityInfo info,
            CancellationToken token) {
        var operation = operations.stream().filter(o -> request.operation().equals(o.name())).findFirst()
                .orElseThrow(() -> new FoundgineActivityFailureException("No Foundgine operation named '"
                        + request.operation() + "' is registered on this worker.", null, "OperationNotRegistered", true));
        FoundgineTemporalDiagnostics.recordOperation(request.operation(), request.compensation());
        try {
            var first = securityFactory.create(request.tenantId(), request.initiator(), token);
            // each Foundgine call inside the operation needs its own single-use warrant
            securityScope.set(first, () -> securityFactory.create(request.tenantId(), request.initiator(), token));
            var context = new FoundgineOperationContext(request.tenantId(), request.initiator(), request.correlationId(),
                    request.payloadJson(), request.outputs(), info.workflowId() + ":" + info.activityId(),
                    request.compensation(), info.attempt());
            return operation.execute(context, token);
        } catch (FoundgineActivityFailureException | CancellationException ex) {
            throw ex;
        } catch (FoundgineNonRetryableOperationException ex) {
            throw new FoundgineActivityFailureException(ex.getMessage(), ex, "OperationRejected", true);
        } catch (SecurityException ex) {
            // authorization failures are deterministic; retrying cannot fix them
            throw new FoundgineActivityFailureException(ex.getMessage(), ex, "Unauthorized", true);
        } catch (RuntimeException ex) {
            throw ex;
        } catch (Exception ex) {
            throw new RuntimeException(ex.getMessage(), ex);
        } finally {
            securityScope.clear();
        }
    }
}
