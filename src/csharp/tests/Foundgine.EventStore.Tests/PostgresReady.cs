using System.Diagnostics;
using System.Net.Sockets;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Foundgine.EventStore.Tests;

/// <summary>
/// Waits until a Testcontainers PostgreSQL accepts a real login over its mapped host port. The container's own readiness
/// probe passes before the host-side port forwarding is usable on some setups (for example WSL with Rancher Desktop), which
/// shows up as intermittent "Connection refused" in the fixture's first connection.
/// </summary>
internal static class PostgresReady
{
    /// <summary>
    /// Starts a container and connects to it, replacing the container with a fresh one if it cannot be reached. An
    /// unreachable container here is a per-container, intermittent environment fault (host port forwarding for a freshly
    /// published port), so a new container normally works. After the last attempt the error lists every attempt.
    /// </summary>
    public static async Task<(PostgreSqlContainer Container, NpgsqlDataSource DataSource)> StartAsync(
        PostgreSqlContainer first, Func<PostgreSqlContainer> create, int attempts = 3)
    {
        var container = first;
        var failures = new List<string>();
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await container.StartAsync();
                return (container, await ConnectAsync(container));
            }
            catch (Exception ex) when (attempt < attempts)
            {
                failures.Add($"attempt {attempt}: {ex.Message}");
                try { await container.DisposeAsync(); } catch { /* best effort: the replacement is what matters */ }
                container = create();
            }
            catch (Exception ex)
            {
                try { await container.DisposeAsync(); } catch { /* best effort */ }
                failures.Add($"attempt {attempt}: {ex.Message}");
                throw new InvalidOperationException(
                    $"PostgreSQL container unreachable after {attempts} attempts. " + string.Join(" || ", failures), ex);
            }
        }
    }

    /// <summary>
    /// Connects to a started container. Tries the mapped host port first; if that stays unreachable (host port forwarding
    /// that is not usable even though the container is healthy) it falls back to the container's own bridge address. If
    /// both fail, the error carries the container state and the tail of its logs so the failure explains itself.
    /// </summary>
    public static async Task<NpgsqlDataSource> ConnectAsync(PostgreSqlContainer container)
    {
        var clock = Stopwatch.StartNew();
        var mapped = NpgsqlDataSource.Create(container.GetConnectionString());
        try
        {
            await WaitAsync(mapped, TimeSpan.FromSeconds(10));
            return mapped;
        }
        catch (Exception first) when (first is NpgsqlException or SocketException or IOException)
        {
            await mapped.DisposeAsync();
            string detail;
            try
            {
                var direct = NpgsqlDataSource.Create(new NpgsqlConnectionStringBuilder(container.GetConnectionString())
                    { Host = container.IpAddress, Port = 5432 }.ConnectionString);
                try
                {
                    await WaitAsync(direct, TimeSpan.FromSeconds(5));
                    return direct;
                }
                catch (Exception second) when (second is NpgsqlException or SocketException or IOException)
                {
                    await direct.DisposeAsync();
                    detail = $"container address {container.IpAddress}:5432 also failed ({second.Message})";
                }
            }
            catch (Exception ex) when (ex is not NpgsqlException and not SocketException and not IOException)
            {
                detail = $"no container address to fall back to ({ex.Message})";
            }

            string logs;
            try
            {
                var (stdout, stderr) = await container.GetLogsAsync();
                var all = (stdout + "\n" + stderr).Split('\n', StringSplitOptions.RemoveEmptyEntries);
                logs = string.Join(" | ", all.TakeLast(15));
            }
            catch (Exception ex)
            {
                logs = $"(logs unavailable: {ex.Message})";
            }

            throw new InvalidOperationException(
                $"PostgreSQL container not reachable after {clock.Elapsed.TotalSeconds:F0}s: mapped port failed ({first.Message}); {detail}. " +
                $"Container state: {container.State}. Last log lines: {logs}", first);
        }
    }

    public static async Task WaitAsync(NpgsqlDataSource dataSource, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(30));
        while (true)
        {
            try
            {
                await using var connection = await dataSource.OpenConnectionAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT 1";
                await command.ExecuteScalarAsync();
                return;
            }
            catch (Exception ex) when ((ex is NpgsqlException or SocketException or IOException) && DateTime.UtcNow < deadline)
            {
                await Task.Delay(250);
            }
        }
    }
}