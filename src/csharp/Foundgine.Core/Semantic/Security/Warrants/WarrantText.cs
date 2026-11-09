namespace Foundgine.Core.Semantic.Security.Warrants;

/// <summary>
/// The single definition of "blank" for warrant fields that feed the canonical form. Runtimes disagree on Unicode
/// whitespace (.NET <c>char.IsWhiteSpace</c> vs Java <c>Character.isWhitespace</c>: U+00A0, U+0085, U+001C-U+001F,
/// U+2007, U+202F), which would make the same input normalize differently. Blank here means empty or made only of
/// U+0009-U+000D and U+0020. Mirrored by <c>WarrantText.isBlank</c> in Java; see src/conformance/README.md.
/// </summary>
internal static class WarrantText
{
    public static bool IsBlank(string? value)
    {
        if (string.IsNullOrEmpty(value)) return true;
        foreach (var c in value)
            if (c != ' ' && (c < '\t' || c > '\r'))
                return false;
        return true;
    }
}
