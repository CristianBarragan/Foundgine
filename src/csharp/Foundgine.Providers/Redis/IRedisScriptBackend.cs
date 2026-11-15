namespace Foundgine.Providers.Redis;

/// <summary>
/// The one operation the shared MCP session store needs from a Redis-protocol server (Redis, Valkey, Microsoft Garnet or any
/// compatible service): run a Lua script atomically (<c>EVAL</c>).
/// </summary>
/// <remarks>
/// Every script the store runs returns a flat array of strings, so an implementation only maps the reply to a list of strings.
/// An unreachable server or an error reply must throw; the store surfaces it as a failed request, never as a valid session.
/// </remarks>
public interface IRedisScriptBackend
{
    IReadOnlyList<string> Eval(string script, IReadOnlyList<string> keys, IReadOnlyList<string> args);
}
