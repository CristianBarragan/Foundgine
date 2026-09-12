package com.foundgine.core.semantic.security.warrants;

import java.io.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.security.MessageDigest;
import java.time.Duration;
import java.time.Instant;

public final class FileSecurityWarrantReplayStore implements ISecurityWarrantReplayStore {
	private static final Duration LOCK_TIMEOUT = Duration.ofSeconds(30), STALE_LOCK_AGE = Duration.ofMinutes(2);
	private final Path path, lockPath;

	public FileSecurityWarrantReplayStore(String value) {
		if (value == null || value.isBlank())
			throw new IllegalArgumentException("A replay store path is required.");
		Path p = Path.of(value).toAbsolutePath();
		Path l = Path.of(p + ".lock");
		try {
			Path parent = p.getParent();
			if (parent != null)
				Files.createDirectories(parent);
			Files.createFile(p);
		} catch (FileAlreadyExistsException e) {
		} catch (IOException e) {
			throw new IllegalStateException("Unable to initialize replay store.", e);
		}
		path = p;
		lockPath = l;
	}

	public boolean tryConsume(String id, String nonce) {
		if (id == null || id.isBlank() || nonce == null || nonce.isBlank())
			throw new IllegalArgumentException();
		String identity;
		try {
			identity = java.util.HexFormat.of().withUpperCase().formatHex(MessageDigest.getInstance("SHA-256")
					.digest((id + "\u001f" + nonce).getBytes(StandardCharsets.UTF_8)));
		} catch (Exception e) {
			throw new IllegalStateException(e);
		}
		try (var lock = acquireLock()) {
			for (String line : Files.readAllLines(path, StandardCharsets.UTF_8))
				if (line.equals(identity))
					return false;
			Files.writeString(path, identity + System.lineSeparator(), StandardCharsets.UTF_8,
					StandardOpenOption.CREATE, StandardOpenOption.APPEND);
			return true;
		} catch (IOException e) {
			throw new IllegalStateException("Unable to update replay store.", e);
		}
	}

	private FileChannelLock acquireLock() throws IOException {
		Instant start = Instant.now();
		while (true) {
			try {
				return new FileChannelLock(
						Files.newByteChannel(lockPath, StandardOpenOption.CREATE_NEW, StandardOpenOption.WRITE),
						lockPath);
			} catch (FileAlreadyExistsException e) {
				if (Duration.between(start, Instant.now()).compareTo(LOCK_TIMEOUT) >= 0) {
					try {
						if (Duration.between(Files.getLastModifiedTime(lockPath).toInstant(), Instant.now())
								.compareTo(STALE_LOCK_AGE) >= 0)
							Files.deleteIfExists(lockPath);
					} catch (IOException ignored) {
					}
					if (Duration.between(start, Instant.now()).compareTo(LOCK_TIMEOUT) >= 0)
						throw new IOException("Timed out acquiring the replay store lock '" + lockPath + "'.");
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
