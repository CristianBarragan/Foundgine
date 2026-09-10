namespace Foundgine.Runtime.ControlPlane.RiskScoring;

/// <summary>Coarse risk classification used by policy and approval decisions.</summary>
public enum RiskTier
{
    Low,
    Medium,
    High,
    Critical,
}

/// <summary>
/// One contributing factor to a risk score. Signals are the unit of
/// explanation: a score is always traceable back to the signals that
/// produced it, never an opaque number.
/// </summary>
public sealed record RiskSignal(string Name, double Weight, string Reason);

/// <summary>
/// The outcome of risk evaluation for a tool call: a tier, a numeric value
/// for ordering/thresholding, and the signals that produced it. Never
/// constructed with a bare number — <see cref="Aggregate"/> is the only
/// path that assembles one from signals, so a score can't exist without its
/// explanation.
/// </summary>
public sealed record RiskScore(RiskTier Tier, double Value, IReadOnlyList<RiskSignal> Signals)
{
    public static RiskScore None { get; } = new(RiskTier.Low, 0, Array.Empty<RiskSignal>());

    public static RiskScore Aggregate(IReadOnlyList<RiskSignal> signals)
    {
        ArgumentNullException.ThrowIfNull(signals);
        if (signals.Count == 0)
            return None;

        var value = signals.Sum(s => s.Weight);
        var tier = value switch
        {
            >= 0.85 => RiskTier.Critical,
            >= 0.6 => RiskTier.High,
            >= 0.3 => RiskTier.Medium,
            _ => RiskTier.Low,
        };
        return new RiskScore(tier, value, signals);
    }
}