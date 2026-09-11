package com.foundgine.core.execution;

import com.foundgine.core.execution.security.*;
import com.foundgine.core.semantic.ir.*;
import com.foundgine.core.semantic.planning.ExecutionOperation;
import com.foundgine.core.semantic.security.SecurityInvariantIds;
import org.junit.jupiter.api.Test;

import java.util.List;

import static org.junit.jupiter.api.Assertions.*;

/** Parity tests for concrete provider security certification and execution binding. */
class ProviderExecutableConformanceParityTest {

    @Test
    void certificationResultRequiresEveryRequiredInvariant() {
        var result = new ProviderSecurityConformanceResult(
                "test",
                List.of(SecurityInvariantIds.AUTHORIZATION_REQUIRED, SecurityInvariantIds.TENANT_ISOLATION),
                List.of(SecurityInvariantIds.AUTHORIZATION_REQUIRED),
                List.of());

        assertFalse(result.isSatisfied());
        assertThrows(IllegalStateException.class, result::ensureSatisfied);
    }

    @Test
    void certificationResultFailsOnProviderViolation() {
        var result = new ProviderSecurityConformanceResult(
                "test",
                List.of(SecurityInvariantIds.AUTHORIZATION_REQUIRED),
                List.of(SecurityInvariantIds.AUTHORIZATION_REQUIRED),
                List.of("authorization predicate was lost"));

        assertFalse(result.isSatisfied());
    }

    @Test
    void gateRejectsExecutableConformanceViolation() {
        var ir = testIr(SecurityInvariantIds.AUTHORIZATION_REQUIRED);
        var compiler = new HostileCertifiedCompiler();
        var plan = compiler.compile(ir);

        assertThrows(IllegalStateException.class,
                () -> SecurityInvariantProofGate.attachAndValidate(plan, ir, compiler));
    }

    @Test
    void providerProfileAloneCannotCrossSecurityCriticalBoundary() {
        var ir = testIr(SecurityInvariantIds.AUTHORIZATION_REQUIRED);
        var compiler = new ProfileOnlyCompiler();

        var exception = assertThrows(IllegalStateException.class,
                () -> SecurityInvariantProofGate.attachAndValidate(compiler.compile(ir), ir, compiler));
        assertTrue(exception.getMessage().toLowerCase().contains("no concrete security conformance evaluator"));
    }

    @Test
    void certificateIsBoundToExactReturnedPlanInstance() {
        var ir = testIr(SecurityInvariantIds.AUTHORIZATION_REQUIRED);
        var compiler = new HonestCompiler();
        var certified = SecurityInvariantProofGate.attachAndValidate(compiler.compile(ir), ir, compiler);
        var transplanted = new TestPlan(certified.provider());
        transplanted.setSecurityProof(certified.securityProof());

        var exception = assertThrows(IllegalStateException.class,
                () -> SecurityInvariantExecutionGate.ensureExecutable(transplanted, ir));
        assertTrue(exception.getMessage().toLowerCase().contains("exact provider plan"));
    }

    @Test
    void certificateCannotBeReplayedAgainstDifferentExecutionIr() {
        var ir = testIr(SecurityInvariantIds.AUTHORIZATION_REQUIRED);
        var compiler = new HonestCompiler();
        var certified = SecurityInvariantProofGate.attachAndValidate(compiler.compile(ir), ir, compiler);
        var differentIr = testIr(SecurityInvariantIds.TENANT_ISOLATION);

        var exception = assertThrows(IllegalStateException.class,
                () -> SecurityInvariantExecutionGate.ensureExecutable(certified, differentIr));
        assertTrue(exception.getMessage().toLowerCase().contains("exact provider plan and execution ir"));
    }

    @Test
    void providerIdentityMismatchIsRejected() {
        var ir = testIr(SecurityInvariantIds.AUTHORIZATION_REQUIRED);
        var compiler = new HonestCompiler();
        var certified = SecurityInvariantProofGate.attachAndValidate(compiler.compile(ir), ir, compiler);
        var mismatched = new TestPlan("different-provider");
        mismatched.setSecurityProof(certified.securityProof());

        var exception = assertThrows(IllegalStateException.class,
                () -> SecurityInvariantExecutionGate.ensureExecutable(mismatched, ir));
        assertTrue(exception.getMessage().toLowerCase().contains("does not match"));
    }

    private static ExecutionIR testIr(String invariant) {
        var node = new ExecutionIRNode(1, ExecutionOperation.SCAN,
                new com.foundgine.core.abstractions.EntityId(1), List.of(),
                null, null, List.of(), null, null, null);
        return new ExecutionIR(node, List.of(invariant), new com.foundgine.core.semantic.planning.SemanticPlanAuthorizationBinding("contract", "authorization"));
    }

    private static final class TestPlan extends ProviderPlan {
        TestPlan(String provider) { super(provider); }
    }

    private static final class HostileCertifiedCompiler implements IProviderPlanCompiler,
            ISecurityInvariantProviderCompiler, IProviderSecurityConformanceEvaluator {
        @Override public java.util.Collection<String> preservedSecurityInvariants() {
            return List.of(SecurityInvariantIds.AUTHORIZATION_REQUIRED);
        }
        @Override public ProviderPlan compile(ExecutionIR ir) { return new TestPlan("hostile-certified"); }
        @Override public ProviderSecurityConformanceResult evaluate(ExecutionIR ir, ProviderPlan plan) {
            return new ProviderSecurityConformanceResult(plan.provider(), ir.requiredSecurityInvariants(), List.of(),
                    List.of("compiled provider plan lost authorization predicate"));
        }
    }

    private static final class ProfileOnlyCompiler implements IProviderPlanCompiler, ISecurityInvariantProviderCompiler {
        @Override public java.util.Collection<String> preservedSecurityInvariants() {
            return List.of(SecurityInvariantIds.AUTHORIZATION_REQUIRED);
        }
        @Override public ProviderPlan compile(ExecutionIR ir) { return new TestPlan("weak"); }
    }

    private static final class HonestCompiler implements IProviderPlanCompiler,
            ISecurityInvariantProviderCompiler, IProviderSecurityConformanceEvaluator {
        @Override public java.util.Collection<String> preservedSecurityInvariants() {
            return List.of(SecurityInvariantIds.AUTHORIZATION_REQUIRED);
        }
        @Override public ProviderPlan compile(ExecutionIR ir) { return new TestPlan("honest"); }
        @Override public ProviderSecurityConformanceResult evaluate(ExecutionIR ir, ProviderPlan plan) {
            return new ProviderSecurityConformanceResult(plan.provider(), ir.requiredSecurityInvariants(),
                    ir.requiredSecurityInvariants(), List.of());
        }
    }
}
