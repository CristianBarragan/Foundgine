using System.Security.Cryptography;
using System.Text;
using Foundgine.Core.Execution;
using Foundgine.Core.Semantic.Metadata;
using Foundgine.Core.Semantic.Planning;
using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.Authorization;
using Foundgine.Core.Semantic.IR;
using Foundgine.Providers.Storage.Sql;
using Npgsql;
using Foundgine.Providers.Storage.Sql.Query;
using ExecutionContext = Foundgine.Core.Execution.ExecutionContext;

namespace Foundgine.SupplyChain.Infrastructure.Queries;

public sealed class SemanticSqlQueryExecutor
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly Planner _planner;
    private readonly IMetadataProvider _metadata;
    private readonly SemanticContractSnapshot _contract;

    public SemanticSqlQueryExecutor(
        NpgsqlDataSource dataSource,
        Planner planner,
        IMetadataProvider metadata,
        SemanticContractSnapshot contract)
    {
        _dataSource = dataSource;
        _planner = planner;
        _metadata = metadata;
        _contract = contract;
    }

    /// <summary>
    /// Executes a canonical operation for the legacy SupplyChain sample.
    ///
    /// The application capability layer is the caller-facing authorization
    /// boundary for this sample. The executor still converts the operation into
    /// an authorization-bound result before planning, so the provider never
    /// receives an unbound operation.
    /// </summary>
    public Task<(IReadOnlyList<ExecutionRow> Rows, string Fingerprint)> ExecuteAsync(
        SemanticOperation operation,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(operation);

        var authorization = new SemanticAuthorizer(
                new AllowAllSemanticAuthorizationPolicy())
            .AuthorizeWithEvidence(_contract, operation);

        return ExecuteAsync(authorization, ct);
    }

    public async Task<(IReadOnlyList<ExecutionRow> Rows, string Fingerprint)> ExecuteAsync(
        SemanticAuthorizationResult authorization,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(authorization);

        // The authorization result is the security boundary.
        // Do not extract the operation and call Plan(operation), because that
        // deliberately produces an executable plan without authorization
        // provenance.
        authorization.EnsureMatches(_contract);

        var semanticPlan = _planner.Plan(_contract, authorization);

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
                        sql + "|" +
                        string.Join(
                            ';',
                            parameters.Select(x =>
                                $"{x.Name}:{x.Value}")))))
            .ToLowerInvariant()[..24];
}