package com.foundgine.samples.supplychain.advanced.ambiguity;

import java.util.LinkedHashMap;
import java.util.Locale;
import java.util.Map;

/**
 * Resolves {@code FOUNDGINE_POSTGRES_CONNECTION_STRING} into a JDBC URL,
 * accepting either a {@code jdbc:} URL directly or an ADO.NET-style
 * connection string — the same convention already used by the
 * {@code foundgine-providers} Postgres E2E tests, reused here so the
 * ambiguity-resolution demo capability (backed by
 * {@link TopSupplierOverdueOrdersService}) doesn't need a second env var.
 *
 * <p>
 * Note this deviates deliberately from the C# original, which reads a
 * separate {@code SupplyChainConnectionString} env var for the
 * {@code MCP.Foundgine}/{@code Database} console apps: the Java sample only
 * has one Postgres-backed demo path, so reusing the existing convention
 * avoids a second, easily-confused env var name.
 * </p>
 */
public final class AmbiguityConnectionStrings {
    private AmbiguityConnectionStrings() {
    }

    public static String jdbcUrl(String envVarName) {
        String value = System.getenv(envVarName);
        if (value == null || value.isBlank())
            return null;
        if (value.startsWith("jdbc:"))
            return value;
        return convertAdoStyle(value);
    }

    private static String convertAdoStyle(String value) {
        Map<String, String> parts = new LinkedHashMap<>();
        for (String item : value.split(";")) {
            int equals = item.indexOf('=');
            if (equals <= 0)
                continue;
            parts.put(item.substring(0, equals).trim().toLowerCase(Locale.ROOT), item.substring(equals + 1).trim());
        }
        String host = parts.getOrDefault("host", parts.getOrDefault("server", "localhost"));
        String port = parts.getOrDefault("port", "5432");
        String database = parts.getOrDefault("database", parts.getOrDefault("database name", "postgres"));
        String user = parts.getOrDefault("username", parts.getOrDefault("user id", "postgres"));
        String password = parts.getOrDefault("password", "");
        return "jdbc:postgresql://" + host + ":" + port + "/" + database + "?user=" + encode(user) + "&password="
                + encode(password);
    }

    private static String encode(String value) {
        return value.replace("%", "%25").replace(" ", "%20").replace("&", "%26");
    }
}
