package com.foundgine.providers.tools.mcp.redis;

import io.lettuce.core.ScriptOutputType;
import io.lettuce.core.api.sync.RedisCommands;
import java.util.ArrayList;
import java.util.List;
import java.util.Objects;

/**
 * {@link RedisScriptBackend} over Lettuce. Lettuce is an optional dependency: add {@code io.lettuce:lettuce-core} yourself.
 * Configure a command timeout on the client so a hung server fails the request instead of blocking it.
 */
public final class LettuceScriptBackend implements RedisScriptBackend {
    private final RedisCommands<String, String> commands;

    public LettuceScriptBackend(RedisCommands<String, String> commands) {
        this.commands = Objects.requireNonNull(commands, "commands");
    }

    @Override
    public List<String> eval(String script, List<String> keys, List<String> args) {
        List<Object> reply = commands.eval(script, ScriptOutputType.MULTI, keys.toArray(new String[0]), args.toArray(new String[0]));
        List<String> out = new ArrayList<>(reply == null ? 0 : reply.size());
        if (reply != null) for (Object o : reply) out.add(String.valueOf(o));
        return out;
    }
}
