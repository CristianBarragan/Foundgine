using System.Security.Cryptography;
using System.Text;
using Foundgine.Core.Execution;
using Foundgine.Core.Semantic.Metadata;
using Foundgine.Core.Semantic.Planning;
using Foundgine.Core.Semantic.IR;
using Foundgine.Providers.Storage.Sql;
using Npgsql;
using Foundgine.SupplyChain.Application;
using Foundgine.Providers.Storage.Sql.Query;
using ExecutionContext = Foundgine.Core.Execution.ExecutionContext;

namespace Foundgine.SupplyChain.Infrastructure.Queries;

public sealed class SemanticSqlQueryExecutor
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly Planner _planner;
    private readonly IMetadataProvider _metadata;

    public SemanticSqlQueryExecutor(
        NpgsqlDataSource dataSource,
        Planner planner,
        IMetadataProvider metadata)
    {
        _dataSource = dataSource;
        _planner = planner;
        _metadata = metadata;
    }

    public async Task<(IReadOnlyList<ExecutionRow> Rows, string Fingerprint)> ExecuteAsync(
        SemanticOperation operation,
        CancellationToken ct)
    {
        var semanticPlan = _planner.Plan(operation);

        var sqlPlan = new SqlCompiler(_metadata).Compile(semanticPlan);

        await using var connection =
            await _dataSource.OpenConnectionAsync(ct);

        var result = await new SqlExecutionProvider(connection)
            .ExecuteAsync(
                sqlPlan,
                new ExecutionContext(),
                ct);

        return (
            result.Rows,
            Fingerprint(
                sqlPlan.CommandText,
                sqlPlan.EffectiveParameters));
    }

    private static string Fingerprint(
        string sql,
        IEnumerable<SqlParameterBinding> parameters) =>
        Convert.ToHexString(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(
                        sql +
                        "|" +
                        string.Join(
                            ';',
                            parameters.Select(x => $"{x.Name}:{x.Value}")))))
            .ToLowerInvariant()[..24];
}