using System.Net.Sockets;
using Npgsql;

namespace Foundgine.EventStore.Tests;

/// <summary>
/// Waits until a Testcontainers PostgreSQL accepts a real login over its mapped host port. The container's own readiness
/// probe passes before the host-side port forwarding is usable on some setups (for example WSL with Rancher Desktop), which
/// shows up as intermittent "Connection refused" in the fixture's first connection.
/// </summary>
internal static class PostgresReady
{
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