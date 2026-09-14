using System.Data;
using Foundgine.HighAssurance.Postgres.Execution;
using Npgsql;
using Xunit;

namespace Foundgine.Runtime.ControlPlane.Tests;

public sealed class PostgresAuthorizationSecurityUnitOfWorkTests
{
    [PostgresFact]
    public async Task Failed_transition_rolls_back_every_security_write()
    {
        await using var dataSource = NpgsqlDataSource.Create(PostgresTestEnvironment.ConnectionString);
        await PrepareAsync(dataSource);
        await using var connection = await dataSource.OpenConnectionAsync();
        await using (var clear =
                     new NpgsqlCommand(
                         "TRUNCATE banking.authorization_context_tombstone, banking.authorization_context_writer, banking.authorization_context;",
                         connection))
            await clear.ExecuteNonQueryAsync();

        var uow = new PostgresAuthorizationSecurityUnitOfWork(dataSource);
        var actor = Guid.NewGuid();
        var writer = Guid.NewGuid();
        var credential = "cred-m534";

        await Assert.ThrowsAsync<InvalidOperationException>(() => uow.ExecuteAsync(async (c, tx, ct) =>
        {
            await Exec(c, tx,
                "INSERT INTO banking.authorization_context_writer(writer_id, actor_id, tenant_id, active, database_role, credential_fingerprint) VALUES (@w,@a,1,true,current_user,@cred);",
                ct,
                ("w", writer), ("a", actor), ("cred", credential));
            await Exec(c, tx,
                "INSERT INTO banking.authorization_context(actor_id, tenant_id, allowed, version, fingerprint, integrity_algorithm, integrity_key_id, integrity_tag) VALUES (@a,1,true,1,'fp','HMAC-SHA256/v1','legacy','0000000000000000000000000000000000000000000000000000000000000000');",
                ct, ("a", actor));
            throw new InvalidOperationException("injected transition failure");
        }));

        await using var verify = await dataSource.OpenConnectionAsync();
        Assert.Equal(0,
            await ScalarLong(verify, "SELECT count(*) FROM banking.authorization_context_writer WHERE writer_id=@w;",
                writer));
        Assert.Equal(0,
            await ScalarLong(verify,
                "SELECT count(*) FROM banking.authorization_context WHERE actor_id=@a AND tenant_id=1;", actor));
    }

    [PostgresFact]
    public async Task Committed_transition_publishes_all_rows_as_one_durable_state()
    {
        await using var dataSource = NpgsqlDataSource.Create(PostgresTestEnvironment.ConnectionString);
        await PrepareAsync(dataSource);
        await using var connection = await dataSource.OpenConnectionAsync();
        await using (var clear =
                     new NpgsqlCommand(
                         "TRUNCATE banking.authorization_context_tombstone, banking.authorization_context_writer, banking.authorization_context;",
                         connection))
            await clear.ExecuteNonQueryAsync();

        var uow = new PostgresAuthorizationSecurityUnitOfWork(dataSource);
        var actor = Guid.NewGuid();
        var writer = Guid.NewGuid();

        await uow.ExecuteAsync(async (c, tx, ct) =>
        {
            await Exec(c, tx,
                "INSERT INTO banking.authorization_context_writer(writer_id, actor_id, tenant_id, active, database_role, credential_fingerprint) VALUES (@w,@a,1,true,current_user,'cred');",
                ct, ("w", writer), ("a", actor));
            await Exec(c, tx,
                "INSERT INTO banking.authorization_context(actor_id, tenant_id, allowed, version, fingerprint, integrity_algorithm, integrity_key_id, integrity_tag) VALUES (@a,1,true,1,'fp','HMAC-SHA256/v1','legacy','0000000000000000000000000000000000000000000000000000000000000000');",
                ct, ("a", actor));
        });

        await using var verify = await dataSource.OpenConnectionAsync();
        Assert.Equal(1,
            await ScalarLong(verify, "SELECT count(*) FROM banking.authorization_context_writer WHERE writer_id=@w;",
                writer));
        Assert.Equal(1,
            await ScalarLong(verify,
                "SELECT count(*) FROM banking.authorization_context WHERE actor_id=@a AND tenant_id=1;", actor));
    }

    private static async Task Exec(NpgsqlConnection c, NpgsqlTransaction tx, string sql, CancellationToken ct,
        params (string, object)[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, c, tx);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task<long> ScalarLong(NpgsqlConnection c, string sql, Guid value)
    {
        await using var command = new NpgsqlCommand(sql, c);
        command.Parameters.AddWithValue(sql.Contains("writer_id", StringComparison.Ordinal) ? "w" : "a", value);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static async Task PrepareAsync(NpgsqlDataSource dataSource)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        var sql = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "schema.sql"));
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
        await using var clear =
            new NpgsqlCommand(
                "TRUNCATE banking.transfer_audit, banking.transfer_idempotency, banking.authorization_context, banking.authorization_context_tombstone, banking.authorization_context_writer, banking.bank_account;",
                connection);
        await clear.ExecuteNonQueryAsync();
    }
}