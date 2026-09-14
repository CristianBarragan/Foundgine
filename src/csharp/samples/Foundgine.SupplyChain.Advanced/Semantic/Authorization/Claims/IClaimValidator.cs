using Foundgine.SupplyChain.Advanced.Authorization;

namespace Foundgine.SupplyChain.Advanced.Authorization.Claims;

/// <summary>
/// Validates and parses the raw string value of one recognized claim key.
/// Recognized keys are registered against an instance of this per key in a
/// <see cref="ClaimSchema"/>, instead of being interleaved inside one large
/// switch statement.
/// </summary>
public interface IClaimValidator
{
    /// <summary>The wire-format key this validator is registered for (e.g. "warehouse").</summary>
    string Key { get; }

    /// <summary>
    /// Validates <paramref name="rawValue"/>. Returns the typed, parsed value on
    /// success so callers who want more than the raw string (see
    /// <see cref="ClaimsValidationResult.TypedAccepted"/>) don't have to re-parse it.
    /// </summary>
    (bool Ok, string? Reason, object? Value) Validate(string rawValue);
}

/// <summary>Typed base class for a single claim key's validator.</summary>
public abstract class ClaimValidator<T> : IClaimValidator
{
    protected ClaimValidator(string key) => Key = key;

    public string Key { get; }

    public abstract (bool Ok, string? Reason, T? Value) Parse(string rawValue);

    (bool Ok, string? Reason, object? Value) IClaimValidator.Validate(string rawValue)
    {
        var (ok, reason, value) = Parse(rawValue);
        return (ok, reason, value);
    }
}

/// <summary>A <see cref="ClaimValidator{T}"/> built from a plain parsing function, to avoid a class-per-key.</summary>
public sealed class DelegateClaimValidator<T> : ClaimValidator<T>
{
    private readonly Func<string, (bool Ok, string? Reason, T? Value)> _parse;

    public DelegateClaimValidator(string key, Func<string, (bool Ok, string? Reason, T? Value)> parse)
        : base(key)
    {
        _parse = parse ?? throw new ArgumentNullException(nameof(parse));
    }

    public override (bool Ok, string? Reason, T? Value) Parse(string rawValue) => _parse(rawValue);
}