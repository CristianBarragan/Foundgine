using Foundgine.Core.Abstractions;
using System.Data;
using System.Data.Common;
using Foundgine.Core.Execution;
using Foundgine.Core.Execution.Mutation;
using Foundgine.Core.Semantic.Metadata;
using Foundgine.Core.Semantic.Planning.Mutation;
using Foundgine.Providers.Storage.Sql.Query;
using ExecutionContext = Foundgine.Core.Execution.ExecutionContext;

namespace Foundgine.Providers.Storage.Sql.Mutation;

/// <summary>
/// Executes one compiled mutation through ADO.NET and materializes RETURNING
/// values into the provider-neutral MutationResult.
/// </summary>
public sealed class SqlMutationExecutionProvider : IMutationExecutionProvider, IMutationBatchExecutionProvider,
    IMutationSecurityConformanceEvaluator
{
    private readonly DbConnection _connection;
    private readonly DbTransaction? _transaction;
    private readonly SqlMutationCompiler? _compiler;

    public MutationSecurityConformanceResult Evaluate(ExecutionMutationIR ir)
    {
        ArgumentNullException.ThrowIfNull(ir);

        if (_compiler is null)
            throw new InvalidOperationException(
                "Mutation security conformance requires metadata. Construct SqlMutationExecutionProvider with an IMetadataProvider.");

        // The concrete SQL compiler emits parameter bindings for every mutation
        // value, and batch execution owns/participates in a transaction spanning
        // the complete batch. These are provider guarantees, not declarations.
        var plan = _compiler.Compile(ir);
        var satisfied = new List<string>();
        var violations = new List<string>();

        var parameterized = plan.Operations
            .Select((operation, index) => new { operation, semantic = ir.Operations[index] })
            .All(x => x.semantic.Fields.Count == 0 || x.operation.Parameters.Count >= x.semantic.Fields.Count);

        if (parameterized)
            satisfied.Add(Foundgine.Core.Semantic.Security.SecurityInvariantIds.ParameterizedValues);
        else
            violations.Add("SQL mutation compilation did not parameterize all mutation field values.");

        satisfied.Add(Foundgine.Core.Semantic.Security.SecurityInvariantIds.AtomicMutation);

        return new MutationSecurityConformanceResult(
            GetType().FullName ?? GetType().Name,
            satisfied,
            violations);
    }

    public SqlMutationExecutionProvider(
        DbConnection connection,
        DbTransaction? transaction = null,
        IMetadataProvider? metadata = null)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _transaction = transaction;
        _compiler = metadata is null ? null : new SqlMutationCompiler(metadata);
    }

    /// <summary>
    /// Canonical batch execution entry point. Physical SQL lowering is owned by
    /// this provider and therefore occurs only after the execution-IR boundary.
    /// </summary>
    public MutationBatchResult ExecuteBatch(
        ExecutionMutationIR ir,
        ExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(ir);
        ArgumentNullException.ThrowIfNull(context);

        if (_compiler is null)
            throw new InvalidOperationException(
                "Executing mutation IR requires metadata. Construct SqlMutationExecutionProvider " +
                "with an IMetadataProvider.");

        return ExecuteBatch(_compiler.Compile(ir), context);
    }

    public MutationBatchResult ExecuteBatch(
        ExecutionMutationIR ir,
        ExecutionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(ir);
        ArgumentNullException.ThrowIfNull(context);

        if (_compiler is null)
            throw new InvalidOperationException(
                "Executing mutation IR requires metadata. Construct SqlMutationExecutionProvider " +
                "with an IMetadataProvider.");

        return ExecuteBatch(_compiler.Compile(ir), context, cancellationToken);
    }

    public MutationResult Execute(ProviderMutationPlan plan, ExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan is not SqlMutationPlan sqlPlan)
            throw new ArgumentException("The SQL mutation provider requires a SqlMutationPlan.", nameof(plan));

        if (_connection.State != ConnectionState.Open)
            _connection.Open();

        using var command = _connection.CreateCommand();
        command.Transaction = _transaction;
        command.CommandText = sqlPlan.CommandText;
        foreach (var binding in sqlPlan.Parameters)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@" + binding.Name;
            parameter.Value = ResolveValue(binding, Array.Empty<MutationResult>()) ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }

        var returned = new Dictionary<FieldId, object?>();
        var affectedRows = 0;

        if (sqlPlan.ReturnedFields.Count > 0)
        {
            using var reader = command.ExecuteReader();
            if (reader.Read())
            {
                affectedRows = 1;
                for (var i = 0; i < sqlPlan.ReturnedFields.Count; i++)
                {
                    var binding = sqlPlan.ReturnedFields[i];
                    returned[binding.FieldId] =
                        reader.IsDBNull(i) ? null : reader.GetValue(i);
                }
            }
        }
        else
        {
            affectedRows = command.ExecuteNonQuery();
        }

        if (affectedRows == 0 &&
            returned.Count == 0 &&
            sqlPlan.FallbackCommandText is not null)
        {
            using var fallback = _connection.CreateCommand();
            fallback.Transaction = _transaction;
            fallback.CommandText = sqlPlan.FallbackCommandText;
            foreach (var binding in sqlPlan.Parameters)
            {
                var parameter = fallback.CreateParameter();
                parameter.ParameterName = "@" + binding.Name;
                parameter.Value = ResolveValue(binding, Array.Empty<MutationResult>()) ?? DBNull.Value;
                fallback.Parameters.Add(parameter);
            }

            using var reader = fallback.ExecuteReader();
            if (reader.Read())
            {
                affectedRows = 1;
                for (var i = 0; i < sqlPlan.ReturnedFields.Count; i++)
                {
                    var binding = sqlPlan.ReturnedFields[i];
                    returned[binding.FieldId] =
                        reader.IsDBNull(i) ? null : reader.GetValue(i);
                }
            }
        }

        return new MutationResult(
            affectedRows,
            returned.Count == 0 ? null : returned);
    }

    public MutationBatchResult ExecuteBatch(
        ProviderMutationBatchPlan plan,
        ExecutionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(context);

        if (plan is not SqlMutationBatchPlan sqlBatch)
            throw new ArgumentException(
                "The SQL mutation provider requires a SqlMutationBatchPlan.",
                nameof(plan));

        if (_connection.State != ConnectionState.Open)
            _connection.Open();

        var transaction = _transaction;
        var ownsTransaction = false;
        if (transaction is null)
        {
            transaction = _connection.BeginTransaction();
            ownsTransaction = true;
        }

        var results = new List<MutationResult>(sqlBatch.Operations.Count);

        try
        {
            for (var operationIndex = 0; operationIndex < sqlBatch.Operations.Count; operationIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var sqlPlan = sqlBatch.Operations[operationIndex];
                using var command = _connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = sqlPlan.CommandText;
                foreach (var binding in sqlPlan.Parameters)
                {
                    var parameter = command.CreateParameter();
                    parameter.ParameterName = "@" + binding.Name;
                    parameter.Value = ResolveValue(binding, results) ?? DBNull.Value;
                    ApplyClrType(parameter, binding.ClrType);
                    command.Parameters.Add(parameter);
                }

                using var cancellationRegistration =
                    cancellationToken.Register(static state => ((DbCommand)state!).Cancel(), command);
                var result = ExecuteCommand(command, sqlPlan);
                cancellationToken.ThrowIfCancellationRequested();
                results.Add(result);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (ownsTransaction)
                transaction.Commit();
            return new MutationBatchResult(results);
        }
        catch
        {
            if (ownsTransaction)
            {
                try
                {
                    transaction.Rollback();
                }
                catch
                {
                }
            }

            throw;
        }
        finally
        {
            if (ownsTransaction)
                transaction.Dispose();
        }
    }

    public MutationBatchResult ExecuteBatch(
        ProviderMutationBatchPlan plan,
        ExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(context);

        if (plan is not SqlMutationBatchPlan sqlBatch)
            throw new ArgumentException(
                "The SQL mutation provider requires a SqlMutationBatchPlan.",
                nameof(plan));

        if (_connection.State != ConnectionState.Open)
            _connection.Open();

        var transaction = _transaction;
        var ownsTransaction = false;
        if (transaction is null)
        {
            transaction = _connection.BeginTransaction();
            ownsTransaction = true;
        }

        var results = new List<MutationResult>(sqlBatch.Operations.Count);

        try
        {
            for (var operationIndex = 0; operationIndex < sqlBatch.Operations.Count; operationIndex++)
            {
                var sqlPlan = sqlBatch.Operations[operationIndex];

                using var command = _connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = sqlPlan.CommandText;

                foreach (var binding in sqlPlan.Parameters)
                {
                    var parameter = command.CreateParameter();
                    parameter.ParameterName = "@" + binding.Name;
                    parameter.Value = ResolveValue(binding, results) ?? DBNull.Value;
                    ApplyClrType(parameter, binding.ClrType);
                    command.Parameters.Add(parameter);
                }

                var result = ExecuteCommand(command, sqlPlan);
                results.Add(result);
            }

            if (ownsTransaction)
                transaction.Commit();
            return new MutationBatchResult(results);
        }
        catch
        {
            if (ownsTransaction)
                transaction.Rollback();
            throw;
        }
        finally
        {
            if (ownsTransaction)
                transaction.Dispose();
        }
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

    private static MutationResult ExecuteCommand(
        DbCommand command,
        SqlMutationPlan sqlPlan)
    {
        if (sqlPlan.ReturnedFields.Count == 0)
            return new MutationResult(command.ExecuteNonQuery());

        using var reader = command.ExecuteReader();
        if (reader.Read())
        {
            var returned = new Dictionary<FieldId, object?>();
            for (var i = 0; i < sqlPlan.ReturnedFields.Count; i++)
            {
                var binding = sqlPlan.ReturnedFields[i];
                returned[binding.FieldId] =
                    reader.IsDBNull(i) ? null : reader.GetValue(i);
            }

            return new MutationResult(1, returned);
        }

        if (sqlPlan.FallbackCommandText is null)
            return new MutationResult(0);

        using var fallback = command.Connection!.CreateCommand();
        fallback.Transaction = command.Transaction;
        fallback.CommandText = sqlPlan.FallbackCommandText;
        foreach (DbParameter parameter in command.Parameters)
        {
            var fallbackParameter = fallback.CreateParameter();
            fallbackParameter.ParameterName = parameter.ParameterName;
            fallbackParameter.Value = parameter.Value;
            fallback.Parameters.Add(fallbackParameter);
        }

        using var fallbackReader = fallback.ExecuteReader();
        if (!fallbackReader.Read())
            return new MutationResult(0);

        var fallbackReturned = new Dictionary<FieldId, object?>();
        for (var i = 0; i < sqlPlan.ReturnedFields.Count; i++)
        {
            var binding = sqlPlan.ReturnedFields[i];
            fallbackReturned[binding.FieldId] =
                fallbackReader.IsDBNull(i) ? null : fallbackReader.GetValue(i);
        }

        return new MutationResult(1, fallbackReturned);
    }

    private static object? ResolveValue(
        SqlParameterBinding binding,
        IReadOnlyList<MutationResult> results)
    {
        if (binding.Source is null)
            return binding.Value;

        var sourceIndex = binding.Source.SourceOperationIndex;
        if (sourceIndex < 0 || sourceIndex >= results.Count)
            throw new InvalidOperationException(
                $"Mutation parameter '{binding.Name}' references operation {sourceIndex}, " +
                "which has not produced a result.");

        var source = results[sourceIndex];
        if (source.ReturnedValues is null ||
            !source.ReturnedValues.TryGetValue(binding.Source.SourceField, out var value))
        {
            throw new InvalidOperationException(
                $"Mutation parameter '{binding.Name}' references field " +
                $"'{binding.Source.SourceField.Value}' that was not returned by operation {sourceIndex}.");
        }

        return value;
    }
}