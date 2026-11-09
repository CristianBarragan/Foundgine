package com.foundgine.core.semantic.security.warrants;

/**
 * The single definition of "blank" for warrant fields that feed the canonical form. Runtimes disagree on Unicode
 * whitespace ({@code Character.isWhitespace} vs .NET {@code char.IsWhiteSpace}: U+00A0, U+0085, U+001C-U+001F,
 * U+2007, U+202F), which would make the same input normalize differently. Blank here means empty or made only of
 * U+0009-U+000D and U+0020. Mirrored by {@code WarrantText.IsBlank} in C#; see conformance/README.md.
 */
final class WarrantText {
    private WarrantText() {}

    static boolean isBlank(String value) {
        if (value == null || value.isEmpty()) return true;
        for (int i = 0; i < value.length(); i++) {
            char c = value.charAt(i);
            if (c != ' ' && (c < '\t' || c > '\r')) return false;
        }
        return true;
    }
}
