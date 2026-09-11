package com.foundgine.providers.storage.sql;

/**
 * Converts Foundgine's deterministic named SQL placeholders to JDBC's
 * positional {@code ?} placeholders at the physical execution boundary.
 *
 * <p>The SQL compiler deliberately keeps named placeholders because they make
 * the provider plan and diagnostics deterministic. JDBC PreparedStatement,
 * however, is positional. The rewrite is therefore a physical-provider
 * concern and does not mutate the provider-neutral SQL plan.</p>
 */
public final class JdbcSqlPlaceholderRewriter {
    private JdbcSqlPlaceholderRewriter() { }

    public static String rewrite(String sql) {
        if (sql == null || sql.isEmpty()) return sql;

        StringBuilder result = new StringBuilder(sql.length());
        boolean singleQuoted = false;
        boolean doubleQuoted = false;

        for (int i = 0; i < sql.length(); i++) {
            char c = sql.charAt(i);

            if (c == '\'' && !doubleQuoted) {
                result.append(c);
                if (singleQuoted && i + 1 < sql.length() && sql.charAt(i + 1) == '\'') {
                    result.append(sql.charAt(++i));
                } else {
                    singleQuoted = !singleQuoted;
                }
                continue;
            }
            if (c == '"' && !singleQuoted) {
                result.append(c);
                if (doubleQuoted && i + 1 < sql.length() && sql.charAt(i + 1) == '"') {
                    result.append(sql.charAt(++i));
                } else {
                    doubleQuoted = !doubleQuoted;
                }
                continue;
            }

            if (!singleQuoted && !doubleQuoted && c == '@' && i + 1 < sql.length()) {
                int end = placeholderEnd(sql, i + 1);
                if (end > i + 1) {
                    result.append('?');
                    i = end - 1;
                    continue;
                }
            }
            result.append(c);
        }
        return result.toString();
    }

    private static int placeholderEnd(String sql, int start) {
        if (start >= sql.length()) return start;
        char first = sql.charAt(start);
        if (first == 'p') {
            int i = start + 1;
            if (i >= sql.length() || !Character.isDigit(sql.charAt(i))) return start;
            while (i < sql.length() && Character.isDigit(sql.charAt(i))) i++;
            return i;
        }
        if (sql.startsWith("__fg_", start)) {
            int i = start + "__fg_".length();
            while (i < sql.length() && (Character.isLetterOrDigit(sql.charAt(i)) || sql.charAt(i) == '_')) i++;
            return i > start + "__fg_".length() ? i : start;
        }
        if (sql.startsWith("auth", start)) {
            int i = start + 4;
            if (i >= sql.length() || !Character.isDigit(sql.charAt(i))) return start;
            while (i < sql.length() && Character.isDigit(sql.charAt(i))) i++;
            return i;
        }
        return start;
    }
}
