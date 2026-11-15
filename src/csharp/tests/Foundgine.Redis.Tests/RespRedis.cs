using System.Globalization;
using System.Net.Sockets;
using System.Text;
using Foundgine.Providers.Redis;

namespace Foundgine.Redis.Tests;

/// <summary>Small RESP2 client used only by opt-in live Redis integration tests.</summary>
internal sealed class RespRedis : IRedisScriptBackend, IWarrantReplayBackend
{
    private readonly string _host;
    private readonly int _port;
    private readonly string? _password;
    private readonly int _database;

    private RespRedis(string host, int port, string? password, int database)
        => (_host, _port, _password, _database) = (host, port, password, database);

    public static RespRedis? FromEnvironment()
    {
        var value = Environment.GetEnvironmentVariable("FOUNDGINE_REDIS");
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (!Uri.TryCreate(value.Contains("://", StringComparison.Ordinal) ? value : "redis://" + value, UriKind.Absolute, out var uri))
            throw new InvalidOperationException("FOUNDGINE_REDIS must be host:port or a redis:// URI.");
        var password = string.IsNullOrEmpty(uri.UserInfo) ? null : Uri.UnescapeDataString(uri.UserInfo.Split(':').Last());
        var dbText = uri.AbsolutePath.Trim('/');
        var db = dbText.Length == 0 ? 0 : int.Parse(dbText, CultureInfo.InvariantCulture);
        return new RespRedis(uri.Host, uri.Port > 0 ? uri.Port : 6379, password, db);
    }

    public IReadOnlyList<string> Eval(string script, IReadOnlyList<string> keys, IReadOnlyList<string> args)
    {
        var command = new List<string> { "EVAL", script, keys.Count.ToString(CultureInfo.InvariantCulture) };
        command.AddRange(keys);
        command.AddRange(args);
        var result = Execute(command);
        return result is List<object?> items
            ? items.Select(x => x?.ToString() ?? string.Empty).ToArray()
            : throw new IOException("Redis EVAL returned a non-array reply.");
    }

    public bool SetIfAbsent(string key, TimeSpan ttl)
    {
        var milliseconds = Math.Max(1L, (long)Math.Ceiling(ttl.TotalMilliseconds));
        var result = Execute(["SET", key, "1", "NX", "PX", milliseconds.ToString(CultureInfo.InvariantCulture)]);
        return result?.ToString() == "OK";
    }

    public long Pttl(string key) => long.Parse(Execute(["PTTL", key])?.ToString() ?? "-2", CultureInfo.InvariantCulture);
    public long DbSize() => long.Parse(Execute(["DBSIZE"])?.ToString() ?? "0", CultureInfo.InvariantCulture);
    public IReadOnlyList<string> ScanKeys(string pattern) => Execute(["KEYS", pattern]) is List<object?> values
        ? values.Select(x => x?.ToString() ?? string.Empty).ToArray()
        : throw new IOException("Redis KEYS returned a non-array reply.");
    public void FlushDatabase() => Execute(["FLUSHDB"]);

    private object? Execute(IReadOnlyList<string> args)
    {
        using var client = new TcpClient();
        client.Connect(_host, _port);
        using var stream = client.GetStream();
        if (_password is not null) { Write(stream, ["AUTH", _password]); Read(stream); }
        if (_database != 0) { Write(stream, ["SELECT", _database.ToString(CultureInfo.InvariantCulture)]); Read(stream); }
        Write(stream, args);
        return Read(stream);
    }

    private static void Write(NetworkStream stream, IReadOnlyList<string> values)
    {
        var builder = new StringBuilder().Append('*').Append(values.Count).Append("\r\n");
        foreach (var value in values) builder.Append('$').Append(Encoding.UTF8.GetByteCount(value)).Append("\r\n").Append(value).Append("\r\n");
        var bytes = Encoding.UTF8.GetBytes(builder.ToString());
        stream.Write(bytes);
        stream.Flush();
    }

    private static object? Read(NetworkStream stream)
    {
        var prefix = stream.ReadByte();
        if (prefix < 0) throw new EndOfStreamException("Redis closed the connection.");
        var line = ReadLine(stream);
        return (char)prefix switch
        {
            '+' => line,
            '-' => throw new IOException("Redis error: " + line),
            ':' => long.Parse(line, CultureInfo.InvariantCulture),
            '$' => ReadBulk(stream, int.Parse(line, CultureInfo.InvariantCulture)),
            '*' => ReadArray(stream, int.Parse(line, CultureInfo.InvariantCulture)),
            _ => throw new IOException($"Unknown RESP prefix {(char)prefix}.")
        };
    }

    private static object? ReadBulk(NetworkStream stream, int length)
    {
        if (length < 0) return null;
        var bytes = new byte[length];
        var offset = 0;
        while (offset < length) { var read = stream.Read(bytes, offset, length - offset); if (read == 0) throw new EndOfStreamException(); offset += read; }
        ExpectCrLf(stream);
        return Encoding.UTF8.GetString(bytes);
    }

    private static List<object?> ReadArray(NetworkStream stream, int count)
    {
        var result = new List<object?>(Math.Max(0, count));
        for (var i = 0; i < count; i++) result.Add(Read(stream));
        return result;
    }

    private static string ReadLine(NetworkStream stream)
    {
        var bytes = new List<byte>();
        while (true) { var b = stream.ReadByte(); if (b < 0) throw new EndOfStreamException(); if (b == '\r') { if (stream.ReadByte() != '\n') throw new IOException("Malformed RESP line."); break; } bytes.Add((byte)b); }
        return Encoding.UTF8.GetString(bytes.ToArray());
    }

    private static void ExpectCrLf(NetworkStream stream)
    {
        if (stream.ReadByte() != '\r' || stream.ReadByte() != '\n') throw new IOException("Malformed RESP bulk string.");
    }
}
