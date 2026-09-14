namespace Foundgine.Core.Semantic.Planning;

/// <summary>Provider-neutral estimate of the work introduced by a rewrite.</summary>
public readonly record struct RewriteCost(double EstimatedWork)
{
    public static RewriteCost From(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value < 0)
            throw new ArgumentOutOfRangeException(nameof(value));
        return new RewriteCost(value);
    }
}

/// <summary>Estimated execution benefit of applying a rewrite.</summary>
public readonly record struct RewriteBenefit(double EstimatedBenefit)
{
    public static RewriteBenefit From(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value < 0)
            throw new ArgumentOutOfRangeException(nameof(value));
        return new RewriteBenefit(value);
    }
}

/// <summary>Deterministic score used only to rank currently applicable rules.</summary>
public readonly record struct RewriteScore(double Value)
{
    public static RewriteScore Calculate(RewriteBenefit benefit, RewriteCost cost)
    {
        var denominator = 1d + cost.EstimatedWork;
        return new RewriteScore(benefit.EstimatedBenefit / denominator);
    }
}
