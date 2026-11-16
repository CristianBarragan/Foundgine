package com.foundgine.providers.tools.mcp.redis;

import com.foundgine.providers.security.redis.ReplayBackend;
import java.io.BufferedInputStream;
import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.Socket;
import java.nio.charset.StandardCharsets;
import java.time.Duration;
import java.util.ArrayList;
import java.util.List;

/**
 * Test-only minimal RESP2 client (one short-lived connection per command) so the Redis stores can be exercised against a
 * real Redis, Valkey or Garnet server with nothing but the JDK. Enabled by {@code FOUNDGINE_REDIS=host:port}.
 */
public final class RespRedis implements RedisScriptBackend, ReplayBackend {
    private final String host;
    private final int port;

    public RespRedis(String host, int port) {
        this.host = host;
        this.port = port;
    }

    /** The server named by {@code FOUNDGINE_REDIS} (host:port), or {@code null} when it is not set. */
    public static RespRedis fromEnvironment() {
        String v = System.getenv("FOUNDGINE_REDIS");
        if (v == null || v.isBlank()) return null;
        int i = v.lastIndexOf(':');
        return new RespRedis(v.substring(0, i), Integer.parseInt(v.substring(i + 1)));
    }

    @Override
    public List<String> eval(String script, List<String> keys, List<String> args) {
        List<Object> cmd = new ArrayList<>(List.of("EVAL", script, Integer.toString(keys.size())));
        cmd.addAll(keys);
        cmd.addAll(args);
        Object reply = command(cmd.toArray());
        List<String> out = new ArrayList<>();
        if (reply instanceof List<?> l) for (Object o : l) out.add(String.valueOf(o));
        return out;
    }

    @Override
    public boolean setIfAbsent(String key, Duration ttl) {
        return command("SET", key, "1", "NX", "PX", Long.toString(Math.max(1L, ttl.toMillis()))) != null;
    }

    public Object command(Object... parts) {
        try (Socket s = new Socket(host, port)) {
            s.setSoTimeout(5000);
            OutputStream out = s.getOutputStream();
            ByteArrayOutputStream buf = new ByteArrayOutputStream();
            buf.write(("*" + parts.length + "\r\n").getBytes(StandardCharsets.UTF_8));
            for (Object p : parts) {
                byte[] b = String.valueOf(p).getBytes(StandardCharsets.UTF_8);
                buf.write(("$" + b.length + "\r\n").getBytes(StandardCharsets.UTF_8));
                buf.write(b);
                buf.write("\r\n".getBytes(StandardCharsets.UTF_8));
            }
            out.write(buf.toByteArray());
            out.flush();
            return read(new BufferedInputStream(s.getInputStream()));
        } catch (IOException e) {
            throw new RuntimeException(e);
        }
    }

    private static Object read(InputStream in) throws IOException {
        int type = in.read();
        String line = line(in);
        switch (type) {
            case '+': return line;
            case '-': throw new RuntimeException("Redis error: " + line);
            case ':': return Long.parseLong(line);
            case '$': {
                int n = Integer.parseInt(line);
                if (n < 0) return null;
                byte[] b = in.readNBytes(n);
                in.readNBytes(2);
                return new String(b, StandardCharsets.UTF_8);
            }
            case '*': {
                int n = Integer.parseInt(line);
                if (n < 0) return null;
                List<Object> l = new ArrayList<>();
                for (int i = 0; i < n; i++) l.add(read(in));
                return l;
            }
            default: throw new IOException("Unexpected RESP type: " + type);
        }
    }

    private static String line(InputStream in) throws IOException {
        StringBuilder sb = new StringBuilder();
        int c;
        while ((c = in.read()) != '\r') {
            if (c < 0) throw new IOException("Connection closed");
            sb.append((char) c);
        }
        in.read();
        return sb.toString();
    }
}
