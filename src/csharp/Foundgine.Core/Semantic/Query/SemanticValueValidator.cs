namespace Foundgine.Core.Semantic.Query;

internal static class SemanticValueValidator
{
    public static void Validate(object? value, SemanticField field, string operation)
    {
        if (value is null) return;

        if (value is System.Collections.IEnumerable values && value is not string &&
            operation.Equals("IN", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var item in values)
                ValidateSingle(item, field, operation);
            return;
        }

        ValidateSingle(value, field, operation);
    }

    private static void ValidateSingle(object? value, SemanticField field, string operation)
    {
        if (value is null) return;

        var actual = SemanticType.FromClrType(value.GetType());
        var expected = field.EffectiveSemanticType;
        if (AreCompatible(expected, actual)) return;

        throw new InvalidOperationException(
            $"Semantic {operation} value for field '{field.Name}' has type '{actual}', but the field requires '{expected}'.");
    }

    private static bool AreCompatible(SemanticType expected, SemanticType actual)
    {
        if (expected == actual) return true;
        if (expected is SemanticType.Scalar { Kind: var e } && actual is SemanticType.Scalar { Kind: var a })
        {
            if (e is SemanticScalarKind.Int32 or SemanticScalarKind.Int64 &&
                a is SemanticScalarKind.Int32 or SemanticScalarKind.Int64)
                return true;
            if (e == SemanticScalarKind.Decimal &&
                a is SemanticScalarKind.Int32 or SemanticScalarKind.Int64 or SemanticScalarKind.Decimal)
                return true;
        }

        return false;
    }
}