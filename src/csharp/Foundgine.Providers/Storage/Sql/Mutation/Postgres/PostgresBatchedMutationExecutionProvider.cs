using System.Data;
using System.Data.Common;
using System.Text.Json;
using Foundgine.Core.Abstractions;
using Foundgine.Core.Execution.Mutation;
using Foundgine.Core.Semantic.Metadata;
using Foundgine.Core.Semantic.Planning.Mutation;
using ExecutionContext = Foundgine.Core.Execution.ExecutionContext;

namespace Foundgine.Providers.Storage.Sql.Mutation.Postgres;

/// <summary>
/// Executes a mutation batch as one PostgreSQL statement when the batch can
/// safely be represented by PostgresBatchedMutationCompiler. Otherwise it
/// delegates to the existing sequential SQL mutation provider.
///
/// PostgreSQL selection is explicit: callers construct this provider for a
/// PostgreSQL connection. The provider never inspects the runtime connection
/// type to decide whether PostgreSQL is available.
/// </summary>
public sealed class PostgresBatchedMutationExecutionProvider : IMutationBatchExecutionProvider,
    IMutationSecurityConformanceEvaluator
{
    public MutationSecurityConformanceResult Evaluate(ExecutionMutationIR ir)
    {
        ArgumentNullException.ThrowIfNull(ir);

        // Both the single-statement PostgreSQL path and the sequential fallback
        // use parameter bindings and execute the complete batch inside one
        // transaction/statement boundary. This is concrete provider evidence.
        var batched = _compiler.TryCompile(ir);
        if (batched is null)
            _fallbackCompiler.Compile(ir);

        return new MutationSecurityConformanceResult(
            GetType().FullName ?? GetType().Name,
            [
                Foundgine.Core.Semantic.Security.SecurityInvariantIds.ParameterizedValues,
                Foundgine.Core.Semantic.Security.SecurityInvariantIds.AtomicMutation
            ],
            []);
    }

    private readonly DbConnection _connection;
    private readonly PostgresBatchedMutationCompiler _compiler;
    private readonly SqlMutationCompiler _fallbackCompiler;
    private readonly SqlMutationExecutionProvider _fallbackProvider;
    private readonly DbTransaction? _transaction;

    public PostgresBatchedMutationExecutionProvider(
        DbConnection connection,
        IMetadataProvider metadata,
        DbTransaction? transaction = null)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        ArgumentNullException.ThrowIfNull(metadata);
        _transaction = transaction;

        _compiler = new PostgresBatchedMutationCompiler(metadata);
        _fallbackCompiler = new SqlMutationCompiler(metadata);
        _fallbackProvider = new SqlMutationExecutionProvider(connection, transaction, metadata);
    }

    /// <summary>
    /// Preferred entry point when the caller still has the provider-neutral
    /// mutation batch. Compilation and fallback happen here.
    /// </summary>
    /// <summary>
    /// Canonical execution entry point for mutation IR.
    /// </summary>
    public MutationBatchResult ExecuteBatch(
        ExecutionMutationIR ir,
        ExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(ir);
        ArgumentNullException.ThrowIfNull(context);

        var batched = _compiler.TryCompile(ir);
        return batched is not null
            ? ExecuteBatchedPlan(batched)
            : _fallbackProvider.ExecuteBatch(_fallbackCompiler.Compile(ir), context);
    }

    public MutationBatchResult ExecuteBatch(
        ExecutionMutationIR ir,
        ExecutionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(ir);
        ArgumentNullException.ThrowIfNull(context);

        var batched = _compiler.TryCompile(ir);
        return batched is not null
            ? ExecuteBatchedPlan(batched, cancellationToken)
            : _fallbackProvider.ExecuteBatch(_fallbackCompiler.Compile(ir), context, cancellationToken);
    }

    public MutationBatchResult ExecuteBatch(
        MutationBatchPlan plan,
        ExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(context);

        var batched = _compiler.TryCompile(plan);
        return batched is not null
            ? ExecuteBatchedPlan(batched)
            : _fallbackProvider.ExecuteBatch(_fallbackCompiler.Compile(plan), context);
    }

    /// <summary>
    /// Interface entry point for callers that already have a provider plan.
    /// </summary>
    public MutationBatchResult ExecuteBatch(
        ProviderMutationBatchPlan plan,
        ExecutionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(context);

        return plan switch
        {
            SqlBatchedMutationPlan batched => ExecuteBatchedPlan(batched, cancellationToken),
            SqlMutationBatchPlan sequential => _fallbackProvider.ExecuteBatch(sequential, context, cancellationToken),
            _ => throw new ArgumentException(
                "The PostgreSQL batched mutation provider requires a SqlBatchedMutationPlan " +
                "or SqlMutationBatchPlan.", nameof(plan))
        };
    }

    public MutationBatchResult ExecuteBatch(
        ProviderMutationBatchPlan plan,
        ExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(context);

        return plan switch
        {
            SqlBatchedMutationPlan batched => ExecuteBatchedPlan(batched),
            SqlMutationBatchPlan sequential => _fallbackProvider.ExecuteBatch(sequential, context),
            _ => throw new ArgumentException(
                "The PostgreSQL batched mutation provider requires a SqlBatchedMutationPlan " +
                "or SqlMutationBatchPlan.",
                nameof(plan))
        };
    }

    private MutationBatchResult ExecuteBatchedPlan(SqlBatchedMutationPlan plan,
        CancellationToken cancellationToken = default)
    {
        if (_connection.State != ConnectionState.Open)
            _connection.Open();

        using var command = _connection.CreateCommand();
        command.Transaction = _transaction;
        command.CommandText = plan.CommandText;

        foreach (var binding in plan.Parameters)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@" + binding.Name;
            parameter.Value = binding.Value ?? DBNull.Value;
            ApplyClrType(parameter, binding.ClrType);
            command.Parameters.Add(parameter);
        }

        var groups = plan.Groups.ToDictionary(x => x.GroupId);
        var rowKeys = plan.RowKeys.ToDictionary(x => (x.GroupId, x.Ordinal));
        var results = new MutationResult[plan.OperationCount];
        var seen = new bool[plan.OperationCount];
        var returned = new Dictionary<FieldId, object?>?[plan.OperationCount];
        var affected = new int[plan.OperationCount];

        using var cancellationRegistration =
            cancellationToken.Register(static state => ((DbCommand)state!).Cancel(), command);
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var groupId = reader.GetInt32(0);
            var json = reader.GetString(1);

            if (!groups.TryGetValue(groupId, out var group))
                throw new InvalidOperationException(
                    $"PostgreSQL batched mutation returned unknown group '{groupId}'.");

            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            int operationIndex;
            if (group.IsOrdinalAddressable)
            {
                if (!root.TryGetProperty("__fg_corr", out var ordinalElement))
                    throw new InvalidOperationException(
                        $"Batched mutation group '{groupId}' did not return its __fg_corr correlation value.");

                var ordinal = ordinalElement.GetInt32();
                if (ordinal < 1 || ordinal > group.OperationIndexesByOrdinal.Count)
                    throw new InvalidOperationException(
                        $"Batched mutation group '{groupId}' returned invalid ordinal '{ordinal}'.");

                operationIndex = group.OperationIndexesByOrdinal[ordinal - 1];

                if (!rowKeys.ContainsKey((groupId, ordinal)))
                    throw new InvalidOperationException(
                        $"Batched mutation returned an unmapped row ordinal '{ordinal}' for group '{groupId}'.");
            }
            else
            {
                operationIndex = group.OperationIndexesByOrdinal[0];

                // Update/Delete preserve the legacy provider's one-row result
                // semantics even when the underlying filter affects multiple rows.
                if (seen[operationIndex])
                    continue;
            }

            seen[operationIndex] = true;
            affected[operationIndex] = 1;

            var values = new Dictionary<FieldId, object?>();
            foreach (var property in root.EnumerateObject())
            {
                if (property.Name == "__fg_corr" ||
                    property.Name == "__affected" ||
                    property.Name.StartsWith("r__k_", StringComparison.Ordinal))
                    continue;

                if (!property.Name.StartsWith("r_", StringComparison.Ordinal) ||
                    !ushort.TryParse(property.Name.AsSpan(2), out var fieldValue))
                    continue;

                var fieldId = new FieldId(fieldValue);
                if (!group.ReturnedFieldTypes.TryGetValue(fieldId, out var clrType))
                    continue;

                values[fieldId] = ConvertJsonValue(property.Value, clrType);
            }

            returned[operationIndex] = values.Count == 0 ? null : values;
        }

        cancellationToken.ThrowIfCancellationRequested();
        for (var i = 0; i < plan.OperationCount; i++)
            results[i] = new MutationResult(affected[i], returned[i]);

        // Every batched Create/Upsert operation must produce exactly one result
        // row. Missing rows indicate a collapsed duplicate conflict key or an
        // otherwise ambiguous correlation and must never silently feed the wrong
        // generated value into a downstream operation.
        foreach (var group in plan.Groups.Where(x => x.IsOrdinalAddressable))
        {
            foreach (var operationIndex in group.OperationIndexesByOrdinal)
            {
                if (!seen[operationIndex])
                    throw new InvalidOperationException(
                        $"PostgreSQL batched mutation produced no result for operation {operationIndex}. " +
                        "This usually means two operations collapsed onto the same conflict/correlation key.");
            }
        }

        return new MutationBatchResult(results);
    }

    private static void ApplyClrType(DbParameter parameter, Type? clrType)
    {
        if (clrType is null) return;
        var type = Nullable.GetUnderlyingType(clrType) ?? clrType;
        if (type == typeof(string)) parameter.DbType = System.Data.DbType.String;
        else if (type == typeof(Guid)) parameter.DbType = System.Data.DbType.Guid;
        else if (type == typeof(bool)) parameter.DbType = System.Data.DbType.Boolean;
        else if (type == typeof(byte)) parameter.DbType = System.Data.DbType.Byte;
        else if (type == typeof(sbyte)) parameter.DbType = System.Data.DbType.SByte;
        else if (type == typeof(short)) parameter.DbType = System.Data.DbType.Int16;
        else if (type == typeof(ushort)) parameter.DbType = System.Data.DbType.UInt16;
        else if (type == typeof(int)) parameter.DbType = System.Data.DbType.Int32;
        else if (type == typeof(uint)) parameter.DbType = System.Data.DbType.UInt32;
        else if (type == typeof(long)) parameter.DbType = System.Data.DbType.Int64;
        else if (type == typeof(ulong)) parameter.DbType = System.Data.DbType.UInt64;
        else if (type == typeof(float)) parameter.DbType = System.Data.DbType.Single;
        else if (type == typeof(double)) parameter.DbType = System.Data.DbType.Double;
        else if (type == typeof(decimal)) parameter.DbType = System.Data.DbType.Decimal;
        else if (type == typeof(DateTime)) parameter.DbType = System.Data.DbType.DateTime2;
        else if (type == typeof(DateTimeOffset)) parameter.DbType = System.Data.DbType.DateTimeOffset;
        else if (type == typeof(DateOnly)) parameter.DbType = System.Data.DbType.Date;
        else if (type == typeof(TimeOnly)) parameter.DbType = System.Data.DbType.Time;
    }

    private static object? ConvertJsonValue(JsonElement element, Type clrType)
    {
        if (element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;

        var target = Nullable.GetUnderlyingType(clrType) ?? clrType;

        try
        {
            return JsonSerializer.Deserialize(element.GetRawText(), target);
        }
        catch (JsonException)
        {
            if (target == typeof(Guid) && element.ValueKind == JsonValueKind.String)
                return Guid.Parse(element.GetString()!);

            if (target == typeof(DateTime) && element.ValueKind == JsonValueKind.String)
                return DateTime.Parse(element.GetString()!);

            if (target == typeof(DateTimeOffset) && element.ValueKind == JsonValueKind.String)
                return DateTimeOffset.Parse(element.GetString()!);

            if (target == typeof(decimal) && element.ValueKind == JsonValueKind.Number)
                return element.GetDecimal();

            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number when element.TryGetInt64(out var integer) => integer,
                JsonValueKind.Number => element.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => element.GetRawText()
            };
        }
    }
}