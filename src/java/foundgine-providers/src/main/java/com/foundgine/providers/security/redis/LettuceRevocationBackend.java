package com.foundgine.providers.security.redis;

import io.lettuce.core.ScriptOutputType;
import io.lettuce.core.api.sync.RedisCommands;
import java.util.List;
import java.util.Objects;

/** Lettuce adapter for {@link RevocationBackend}; configure bounded command timeouts on the client. */
public final class LettuceRevocationBackend implements RevocationBackend {
    private final RedisCommands<String, String> commands;

    public LettuceRevocationBackend(RedisCommands<String, String> commands) {
        this.commands = Objects.requireNonNull(commands, "commands");
    }

    @Override
    public List<String> eval(String script, List<String> keys, List<String> arguments) {
        Object result = commands.eval(script, ScriptOutputType.MULTI, keys.toArray(String[]::new), arguments.toArray(String[]::new));
        if (!(result instanceof List<?> values)) throw new IllegalStateException("Redis returned a non-list script result.");
        return values.stream().map(value -> value == null ? "" : value.toString()).toList();
    }
}
