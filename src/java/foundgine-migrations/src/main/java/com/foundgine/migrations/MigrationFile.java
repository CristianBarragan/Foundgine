package com.foundgine.migrations;

import java.nio.file.Path;
import java.util.Optional;
import java.util.regex.Matcher;
import java.util.regex.Pattern;

/** A Flyway versioned migration file, {@code V<version>__<Name>.sql}. */
public record MigrationFile(String version, String name, Path path) {
    private static final Pattern FILE = Pattern.compile("^V([0-9]+(?:[._][0-9]+)*)__(.+)\\.sql$");

    public static Optional<MigrationFile> parse(Path path) {
        Matcher m = FILE.matcher(path.getFileName().toString());
        return m.matches() ? Optional.of(new MigrationFile(m.group(1), m.group(2), path)) : Optional.empty();
    }

    /** Numeric order of the version parts, matching how Flyway orders versions. */
    public int compareVersion(MigrationFile other) {
        String[] a = version.split("[._]");
        String[] b = other.version.split("[._]");
        for (int i = 0; i < Math.max(a.length, b.length); i++) {
            java.math.BigInteger x = i < a.length ? new java.math.BigInteger(a[i]) : java.math.BigInteger.ZERO;
            java.math.BigInteger y = i < b.length ? new java.math.BigInteger(b[i]) : java.math.BigInteger.ZERO;
            int c = x.compareTo(y);
            if (c != 0) return c;
        }
        return 0;
    }
}
