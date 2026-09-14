using Foundgine.Core.Abstractions;
using System.Data.Common;
using System.Diagnostics;
using Foundgine.Core.Execution;
using ExecutionContext = Foundgine.Core.Execution.ExecutionContext;

namespace Foundgine.Providers.Storage.Sql;

/// <summary>
/// Executes an already-compiled SQL plan against an ADO.NET connection.
/// Compilation and execution remain separate responsibilities.
/// </summary>
public sealed class SqlExecutionProvider : IExecutionProvider
{
    private readonly DbConnection _connection;
    private readonly DbTransaction? _transaction;

    public SqlExecutionProvider(DbConnection connection, DbTransaction? transaction = null)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _transaction = transaction;
    }

    public async Task<ExecutionResult> ExecuteAsync(
        ProviderPlan plan,
        ExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan is not SqlPlan sqlPlan)
            throw new ArgumentException("The SQL provider requires a SqlPlan.", nameof(plan));
        if (sqlPlan.AuthorizationBinding is null)
            throw new InvalidOperationException(
                "The SQL provider refuses to execute a provider plan without authorization provenance.");

        var stopwatch = Stopwatch.StartNew();

        if (_connection.State != System.Data.ConnectionState.Open)
            await _connection.OpenAsync(cancellationToken);

        await using var command = _connection.CreateCommand();
        command.Transaction = _transaction;
        command.CommandText = sqlPlan.CommandText;
        foreach (var binding in sqlPlan.EffectiveParameters)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@" + binding.Name;
            object? value = binding.Value;
            if (binding.ContextPath is { } contextPath)
            {
                if (!context.TryGetValue(contextPath, out value))
                    throw new InvalidOperationException(
                        $"Execution context does not contain authorization value '{contextPath}'.");

                // Forward pagination fetches one extra row so the provider can
                // determine HasNextPage without changing the requested page size.
                if (string.Equals(contextPath, ExecutionContextKeys.PaginationLimit, StringComparison.Ordinal) &&
                    sqlPlan.Pagination is not null &&
                    value is not null)
                {
                    value = Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture) + 1;
                }
            }

            parameter.Value = value ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<ExecutionRow>();

        while (await reader.ReadAsync(cancellationToken))
        {
            var values = new Dictionary<string, object?>(StringComparer.Ordinal);
            var cells = new Dictionary<ExecutionCellKey, object?>();

            for (var i = 0; i < reader.FieldCount; i++)
            {
                var name = reader.GetName(i);
                var value = reader.IsDBNull(i) ? null : reader.GetValue(i);
                values[name] = value;

                var binding = sqlPlan.Columns.FirstOrDefault(x =>
                    string.Equals(x.ResultName, name, StringComparison.Ordinal));

                if (binding is not null)
                {
                    cells[new ExecutionCellKey(
                        binding.NodeId,
                        binding.EntityId,
                        binding.FieldId)] = value;
                }
            }

            rows.Add(new ExecutionRow(values, cells));
        }

        ExecutionPageInfo? pageInfo = null;
        if (sqlPlan.Pagination is { } paging)
        {
            var first = paging.First;
            var hasCursor = paging.After is not null;
            if (context.TryGetValue(ExecutionContextKeys.PaginationLimit, out var runtimeLimit) &&
                runtimeLimit is not null)
            {
                first = Convert.ToInt32(runtimeLimit, System.Globalization.CultureInfo.InvariantCulture);
            }

            if (context.TryGetValue(ExecutionContextKeys.PaginationHasCursor, out var cursorValue) &&
                cursorValue is bool runtimeCursor)
                hasCursor = runtimeCursor;

            var hasNext = rows.Count > first;
            if (hasNext) rows.RemoveAt(rows.Count - 1);
            string? start = null;
            string? end = null;
            if (rows.Count > 0)
            {
                var firstValues = paging.CursorValues
                    .Select(binding => rows[0].Values[binding.ResultName])
                    .ToArray();
                var lastValues = paging.CursorValues
                    .Select(binding => rows[^1].Values[binding.ResultName])
                    .ToArray();

                if (firstValues.Any(value => value is null) || lastValues.Any(value => value is null))
                    throw new InvalidOperationException("Cursor ordering fields cannot contain null values.");

                start = Query.CursorCodec.Encode(firstValues);
                end = Query.CursorCodec.Encode(lastValues);
            }

            pageInfo = new ExecutionPageInfo(start, end, hasNext, hasCursor);
        }

        stopwatch.Stop();
        var evidence = ExecutionEvidenceFactory.Create(
            "sql",
            ExecutionEvidenceFactory.Hash(BuildPlanFingerprint(sqlPlan)),
            sqlPlan.Authorization?.Select(x => x.NodeId) ?? [],
            rows.Count,
            stopwatch.ElapsedMilliseconds,
            sqlPlan.CommandText);

        return new ExecutionResult(rows, pageInfo, evidence);
    }

    private static string BuildPlanFingerprint(SqlPlan plan)
    {
        var columns = string.Join(";", plan.Columns.Select(x =>
            $"{x.ResultName}|{x.EntityId.Value}|{x.FieldId.Value}|{x.ColumnName}|{x.NodeId}|{x.IsCursor}"));
        var authorization = string.Join(";", plan.Authorization?.Select(x =>
            $"{x.NodeId}|{x.Predicate}") ?? []);
        return $"{plan.CommandText}|{columns}|{authorization}";
    }
}