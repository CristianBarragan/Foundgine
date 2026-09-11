package com.foundgine.runtime.controlplane.riskscoring;

import java.util.List;
import java.util.Objects;

/**
 * Port of {@code Foundgine.Runtime.ControlPlane.RiskScoring.RiskScore}, together
 * with the {@code RiskTier} enum and {@code RiskSignal} record declared in the
 * same C# file.
 *
 * <p>Never constructed with a bare number — {@link #aggregate} is the only
 * path that assembles one from signals, so a score can't exist without its
 * explanation.
 */
public record RiskScore(RiskTier tier, double value, List<RiskSignal> signals) {

    /** Coarse risk classification used by policy and approval decisions. */
    public enum RiskTier {
        LOW, MEDIUM, HIGH, CRITICAL
    }

    /**
     * One contributing factor to a risk score. Signals are the unit of
     * explanation: a score is always traceable back to the signals that
     * produced it, never an opaque number.
     */
    public record RiskSignal(String name, double weight, String reason) {
    }

    public RiskScore {
        signals = List.copyOf(signals);
    }

    public static final RiskScore NONE = new RiskScore(RiskTier.LOW, 0, List.of());

    public static RiskScore aggregate(List<RiskSignal> signals) {
        Objects.requireNonNull(signals, "signals");
        if (signals.isEmpty()) {
            return NONE;
        }

        var value = signals.stream().mapToDouble(RiskSignal::weight).sum();
        RiskTier tier;
        if (value >= 0.85) {
            tier = RiskTier.CRITICAL;
        } else if (value >= 0.6) {
            tier = RiskTier.HIGH;
        } else if (value >= 0.3) {
            tier = RiskTier.MEDIUM;
        } else {
            tier = RiskTier.LOW;
        }
        return new RiskScore(tier, value, signals);
    }
}
