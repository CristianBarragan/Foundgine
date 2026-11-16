package com.foundgine.providers.security.redis;

import io.lettuce.core.SetArgs;
import io.lettuce.core.api.sync.RedisCommands;
import java.time.Duration;
import java.util.Objects;

/**
 * {@link ReplayBackend} over Lettuce. Lettuce is an optional dependency: add {@code io.lettuce:lettuce-core} yourself.
 * Configure a command timeout on the client so a hung server fails closed instead of blocking requests.
 *
 * <pre>{@code
 * var client = RedisClient.create("redis://garnet:6379");
 * var commands = client.connect().sync();
 * o.warrantReplayStore(new RedisSecurityWarrantReplayStore(new LettuceReplayBackend(commands)));
 * }</pre>
 */
public final class LettuceReplayBackend implements ReplayBackend {
    private final RedisCommands<String, String> commands;

    public LettuceReplayBackend(RedisCommands<String, String> commands) {
        this.commands = Objects.requireNonNull(commands, "commands");
    }

    @Override
    public boolean setIfAbsent(String key, Duration ttl) {
        // SET key 1 NX PX ttl answers "OK" when it created the key and null when the key already existed.
        return commands.set(key, "1", SetArgs.Builder.nx().px(Math.max(1L, ttl.toMillis()))) != null;
    }
}
