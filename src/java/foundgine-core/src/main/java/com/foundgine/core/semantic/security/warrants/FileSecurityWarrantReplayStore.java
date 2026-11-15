package com.foundgine.core.semantic.security.warrants;

import java.io.*;
import java.nio.ByteBuffer;
import java.nio.channels.FileChannel;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.security.MessageDigest;
import java.time.Clock;
import java.time.Duration;
import java.time.Instant;
import java.util.ArrayList;
import java.util.List;
import java.util.Objects;

/**
 * Durable replay protection backed by a lock-protected append-only file, for hosts that share a filesystem. Development
 * and single-node use only: cloud deployments should use a shared transactional store.
 *
 * <p>Each line is {@code HEX} (legacy, no lifetime, kept forever) or {@code HEX<TAB>retainUntilEpochMillis}. Entries
 * consumed with an expiry are dropped once expired; the file is compacted when enough of it has expired, so it stays
 * bounded by the warrants issued within one lifetime instead of growing without limit.
 */
public final class FileSecurityWarrantReplayStore implements ISecurityWarrantReplayStore {
    private static final Duration LOCK_TIMEOUT = Duration.ofSeconds(30),
            STALE_LOCK_AGE = Duration.ofMinutes(2);
    /** Compact once this many expired lines have accumulated, or when at least half the file has expired. */
    private static final int COMPACT_MIN_EXPIRED = 1024;

    private final Path path, lockPath;
    private final Clock clock;

    public FileSecurityWarrantReplayStore(String value) {
        this(value, Clock.systemUTC());
    }

    public FileSecurityWarrantReplayStore(String value, Clock clock) {
        if (value == null || value.isBlank())
            throw new IllegalArgumentException("A replay store path is required.");
        this.clock = Objects.requireNonNull(clock);
        Path p = Path.of(value).toAbsolutePath();
        Path l = Path.of(p + ".lock");
        try {
            Path parent = p.getParent();
            if (parent != null) Files.createDirectories(parent);
            Files.createFile(p);
        } catch (FileAlreadyExistsException e) {
        } catch (IOException e) {
            throw new IllegalStateException("Unable to initialize replay store.", e);
        }
        path = p;
        lockPath = l;
    }

    @Override
    public boolean tryConsume(String id, String nonce) {
        return consume(id, nonce, Long.MAX_VALUE, false);
    }

    @Override
    public boolean tryConsume(String id, String nonce, Instant expiresAt) {
        Objects.requireNonNull(expiresAt);
        return consume(id, nonce, MemorySecurityWarrantReplayStore.retainUntil(expiresAt), true);
    }

    private boolean consume(String id, String nonce, long retainUntil, boolean hasExpiry) {
        if (id == null || id.isBlank() || nonce == null || nonce.isBlank())
            throw new IllegalArgumentException();
        String identity = identity(id, nonce);
        String newLine = hasExpiry ? identity + "\t" + retainUntil : identity;
        long now = clock.millis();
        try (var lock = acquireLock()) {
            List<String> kept = new ArrayList<>();
            int expired = 0;
            for (String line : Files.readAllLines(path, StandardCharsets.UTF_8)) {
                if (line.isEmpty()) continue;
                int tab = line.indexOf('\t');
                String lineIdentity = tab < 0 ? line : line.substring(0, tab);
                if (lineIdentity.equals(identity)) return false;
                if (tab >= 0 && isExpired(line, tab, now)) {
                    expired++;
                    continue;
                }
                kept.add(line);
            }
            if (expired >= COMPACT_MIN_EXPIRED || (expired > 0 && expired >= kept.size())) {
                kept.add(newLine);
                rewrite(kept);
            } else {
                append(newLine);
            }
            return true;
        } catch (IOException e) {
            throw new IllegalStateException("Unable to update replay store.", e);
        }
    }

    private static boolean isExpired(String line, int tab, long now) {
        try {
            return Long.parseLong(line.substring(tab + 1)) <= now;
        } catch (NumberFormatException e) {
            return false; // unreadable lifetime: keep the entry, never silently re-open a consumed warrant
        }
    }

    private static String identity(String id, String nonce) {
        try {
            return java.util.HexFormat.of()
                    .withUpperCase()
                    .formatHex(
                            MessageDigest.getInstance("SHA-256")
                                    .digest((id + "\u001f" + nonce).getBytes(StandardCharsets.UTF_8)));
        } catch (Exception e) {
            throw new IllegalStateException(e);
        }
    }

    private void append(String line) throws IOException {
        byte[] bytes = (line + System.lineSeparator()).getBytes(StandardCharsets.UTF_8);
        try (FileChannel ch =
                FileChannel.open(path, StandardOpenOption.CREATE, StandardOpenOption.WRITE, StandardOpenOption.APPEND)) {
            ch.write(ByteBuffer.wrap(bytes));
            ch.force(true);
        }
    }

    /** Replaces the file with {@code lines} via a temp file in the same directory, so a crash never leaves a partial file. */
    private void rewrite(List<String> lines) throws IOException {
        Path dir = path.getParent();
        Path tmp = Files.createTempFile(dir, path.getFileName().toString(), ".tmp");
        try {
            StringBuilder sb = new StringBuilder();
            for (String l : lines) sb.append(l).append(System.lineSeparator());
            try (FileChannel ch = FileChannel.open(tmp, StandardOpenOption.WRITE, StandardOpenOption.TRUNCATE_EXISTING)) {
                ch.write(ByteBuffer.wrap(sb.toString().getBytes(StandardCharsets.UTF_8)));
                ch.force(true);
            }
            try {
                Files.move(tmp, path, StandardCopyOption.REPLACE_EXISTING, StandardCopyOption.ATOMIC_MOVE);
            } catch (AtomicMoveNotSupportedException e) {
                Files.move(tmp, path, StandardCopyOption.REPLACE_EXISTING);
            }
        } finally {
            Files.deleteIfExists(tmp);
        }
    }

    private FileChannelLock acquireLock() throws IOException {
        Instant start = Instant.now();
        while (true) {
            try {
                return new FileChannelLock(
                        Files.newByteChannel(
                                lockPath, StandardOpenOption.CREATE_NEW, StandardOpenOption.WRITE),
                        lockPath);
            } catch (FileAlreadyExistsException e) {
                if (Duration.between(start, Instant.now()).compareTo(LOCK_TIMEOUT) >= 0) {
                    try {
                        if (Duration.between(
                                                Files.getLastModifiedTime(lockPath).toInstant(),
                                                Instant.now())
                                        .compareTo(STALE_LOCK_AGE)
                                >= 0) Files.deleteIfExists(lockPath);
                    } catch (IOException ignored) {
                    }
                    if (Duration.between(start, Instant.now()).compareTo(LOCK_TIMEOUT) >= 0)
                        throw new IOException(
                                "Timed out acquiring the replay store lock '" + lockPath + "'.");
                }
                try {
                    Thread.sleep(25);
                } catch (InterruptedException x) {
                    Thread.currentThread().interrupt();
                    throw new IOException("Interrupted while acquiring replay store lock.", x);
                }
            }
        }
    }

    private static final class FileChannelLock implements AutoCloseable {
        private final java.nio.channels.SeekableByteChannel channel;
        private final Path path;

        FileChannelLock(java.nio.channels.SeekableByteChannel c, Path p) {
            channel = c;
            path = p;
        }

        public void close() throws IOException {
            try {
                channel.close();
            } finally {
                Files.deleteIfExists(path);
            }
        }
    }
}
