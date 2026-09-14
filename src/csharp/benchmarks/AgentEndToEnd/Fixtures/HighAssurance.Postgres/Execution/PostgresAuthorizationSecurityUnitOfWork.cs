using System.Data;
using Npgsql;

namespace Foundgine.HighAssurance.Postgres.Execution;

/// <summary>
/// Durable linearization boundary for authorization security state.
/// All related authorization/delegation/key/revocation writes must execute on the same
/// PostgreSQL transaction. Application-memory state is only updated after COMMIT.
/// </summary>
public sealed class PostgresAuthorizationSecurityUnitOfWork
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresAuthorizationSecurityUnitOfWork(NpgsqlDataSource dataSource)
        => _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));

    public async Task ExecuteAsync(
        Func<NpgsqlConnection, NpgsqlTransaction, CancellationToken, Task> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction =
            await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            await operation(connection, transaction, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    /// <summary>
    /// Executes a transition and returns a value only after the durable transaction commits.
    /// This prevents callers from publishing an in-memory authorization result that later rolls back.
    /// </summary>
    public async Task<T> ExecuteAsync<T>(
        Func<NpgsqlConnection, NpgsqlTransaction, CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction =
            await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var result = await operation(connection, transaction, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }
}