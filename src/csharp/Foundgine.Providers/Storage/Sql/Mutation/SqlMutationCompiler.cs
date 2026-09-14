using System.Text;
using Foundgine.Core.Semantic.Metadata;
using Foundgine.Core.Execution.Mutation;
using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Planning.Mutation;
using Foundgine.Core.Semantic.Query;
using Foundgine.Providers.Storage.Sql.Query;

namespace Foundgine.Providers.Storage.Sql.Mutation;

/// <summary>
/// Compiles provider-neutral mutation plans into parameterized relational SQL.
/// Upsert uses INSERT ... ON CONFLICT ... DO UPDATE ... RETURNING.
/// </summary>
public sealed class SqlMutationCompiler
{
    private readonly IMetadataProvider _metadata;

    public SqlMutationCompiler(IMetadataProvider metadata) =>
        _metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));

    /// <summary>
    /// Canonical execution entry point. Provider-specific SQL lowering starts
    /// from ExecutionMutationIR; the legacy planning overload remains as an
    /// internal compatibility surface for existing callers.
    /// </summary>
    public SqlMutationBatchPlan Compile(ExecutionMutationIR ir) =>
        Compile(ir.ToMutationBatchPlan());

    public SqlMutationBatchPlan Compile(MutationBatchPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Operations.Count == 0)
            throw new InvalidOperationException("A mutation batch must contain at least one operation.");

        var operations = new List<SqlMutationPlan>(plan.Operations.Count);
        foreach (var operation in plan.Operations)
            operations.Add(Compile(new MutationPlan([operation])));

        return new SqlMutationBatchPlan(operations, plan.Dependencies);
    }

    public SqlMutationPlan Compile(MutationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Operations.Count != 1)
            throw new NotSupportedException("A mutation plan must contain exactly one operation.");

        var operation = plan.Operations[0];
        return operation.Kind switch
        {
            MutationKind.Upsert => CompileUpsert(operation),
            MutationKind.Create => CompileCreate(operation),
            MutationKind.Update => CompileUpdate(operation),
            MutationKind.Delete => CompileDelete(operation),
            _ => throw new NotSupportedException($"Unsupported mutation kind '{operation.Kind}'.")
        };
    }

    private SqlMutationPlan CompileUpsert(MutationOperation op)
    {
        var entity = _metadata.GetEntity(op.Entity.Id);
        var conflicts = op.ConflictColumns?.ToArray()
                        ?? (entity.PrimaryKey is { } pk ? [pk.ColumnId] : Array.Empty<ColumnId>());
        if (conflicts.Length == 0)
            throw new InvalidOperationException($"Upsert '{entity.Name}' has no conflict identity.");

        var fields = op.Fields.ToArray();
        var columnNames = fields.Select(f => ResolveColumn(entity, f.Column)).ToArray();
        var sb = new StringBuilder();
        sb.Append("INSERT INTO ")
            .Append(Table(entity.EffectiveStorageName))
            .Append(" (").Append(string.Join(", ", columnNames.Select(Q))).Append(") VALUES (");

        var parameters = new List<SqlParameterBinding>();
        for (var i = 0; i < fields.Length; i++)
        {
            if (i > 0) sb.Append(", ");
            var name = "p" + i;
            sb.Append("@").Append(name);
            parameters.Add(new SqlParameterBinding(name, fields[i].Value, fields[i].Source,
                ClrType: entity.EffectiveFields.First(f => f.Column is { } c && c.ColumnId == fields[i].Column)
                    .ClrType));
        }

        sb.Append(") ON CONFLICT (")
            .Append(string.Join(", ", conflicts.Select(c => Q(ResolveColumn(entity, c)))))
            .Append(") ");

        var updates = fields
            .Where(f => !conflicts.Contains(f.Column))
            .ToArray();

        string? fallbackCommandText = null;

        if (updates.Length == 0)
        {
            sb.Append("DO NOTHING");
        }
        else
        {
            sb.Append("DO UPDATE SET ");
            for (var i = 0; i < updates.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                var column = ResolveColumn(entity, updates[i].Column);
                var parameterIndex = Array.IndexOf(fields, updates[i]);
                sb.Append(Q(column)).Append(" = @p").Append(parameterIndex);
            }

            // Avoid issuing a physical UPDATE when the incoming values are
            // identical to the stored values. PostgreSQL's IS DISTINCT FROM
            // is null-safe and works for nullable/non-nullable scalar columns.
            //
            // Important: PostgreSQL returns no row from RETURNING when the
            // ON CONFLICT DO UPDATE ... WHERE predicate is false. Foundgine
            // relies on RETURNING for mutation identity/dependency materialization,
            // so generate a fallback SELECT for the no-change case.
            var conflictParameters = conflicts
                .Select(c => (Column: c, FieldIndex: Array.FindIndex(fields, f => f.Column == c)))
                .ToArray();

            var canFallback = op.ReturnFields is { Count: > 0 } &&
                              conflictParameters.All(x => x.FieldIndex >= 0);

            sb.Append(" WHERE ");
            for (var i = 0; i < updates.Length; i++)
            {
                if (i > 0) sb.Append(" OR ");
                var column = ResolveColumn(entity, updates[i].Column);
                sb.Append(Table(entity.EffectiveStorageName))
                    .Append('.')
                    .Append(Q(column))
                    .Append(" IS DISTINCT FROM EXCLUDED.")
                    .Append(Q(column));
            }

            if (canFallback)
            {
                var fallback = new StringBuilder();
                fallback.Append("SELECT ");
                for (var i = 0; i < op.ReturnFields!.Count; i++)
                {
                    if (i > 0) fallback.Append(", ");
                    var field = entity.EffectiveFields.FirstOrDefault(f => f.Id == op.ReturnFields[i])
                                ?? throw new InvalidOperationException(
                                    $"Unknown return field '{op.ReturnFields[i]}'.");
                    var column = ResolveColumn(entity, field.Column!.ColumnId);
                    var resultName = "r_" + field.Id.Value;
                    fallback.Append(Table(entity.EffectiveStorageName))
                        .Append('.')
                        .Append(Q(column))
                        .Append(" AS ")
                        .Append(Q(resultName));
                }

                fallback.Append(" FROM ")
                    .Append(Table(entity.EffectiveStorageName))
                    .Append(" WHERE ");

                for (var i = 0; i < conflictParameters.Length; i++)
                {
                    if (i > 0) fallback.Append(" AND ");
                    var column = ResolveColumn(entity, conflictParameters[i].Column);
                    fallback.Append(Q(column))
                        .Append(" IS NOT DISTINCT FROM @p")
                        .Append(conflictParameters[i].FieldIndex);
                }

                fallback.Append(" LIMIT 1");
                fallbackCommandText = fallback.ToString();
            }
        }

        AppendReturning(sb, entity, op.ReturnFields, out var returns);
        return new SqlMutationPlan(sb.ToString(), parameters, returns, fallbackCommandText);
    }

    private SqlMutationPlan CompileCreate(MutationOperation op)
    {
        var entity = _metadata.GetEntity(op.Entity.Id);
        var fields = op.Fields.ToArray();
        var sb = new StringBuilder("INSERT INTO ");
        sb.Append(Table(entity.EffectiveStorageName)).Append(" (")
            .Append(string.Join(", ", fields.Select(f => Q(ResolveColumn(entity, f.Column)))))
            .Append(") VALUES (");
        var parameters = new List<SqlParameterBinding>();
        for (var i = 0; i < fields.Length; i++)
        {
            if (i > 0) sb.Append(", ");
            var name = "p" + i;
            sb.Append("@").Append(name);
            parameters.Add(new SqlParameterBinding(name, fields[i].Value, fields[i].Source,
                ClrType: entity.EffectiveFields.First(f => f.Column is { } c && c.ColumnId == fields[i].Column)
                    .ClrType));
        }

        sb.Append(')');
        AppendReturning(sb, entity, op.ReturnFields, out var returns);
        return new SqlMutationPlan(sb.ToString(), parameters, returns);
    }

    private SqlMutationPlan CompileUpdate(MutationOperation op)
    {
        if (op.Filter is null) throw new InvalidOperationException("Update requires a filter.");
        var entity = _metadata.GetEntity(op.Entity.Id);
        var sb = new StringBuilder("UPDATE ");
        sb.Append(Table(entity.EffectiveStorageName)).Append(" SET ");
        var parameters = new List<SqlParameterBinding>();
        for (var i = 0; i < op.Fields.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            var name = "p" + i;
            sb.Append(Q(ResolveColumn(entity, op.Fields[i].Column))).Append(" = @").Append(name);
            parameters.Add(new SqlParameterBinding(name, op.Fields[i].Value, op.Fields[i].Source,
                ClrType: entity.EffectiveFields.First(f => f.Column is { } c && c.ColumnId == op.Fields[i].Column)
                    .ClrType));
        }

        var alias = "t0";
        sb.Append(" WHERE ");
        var where = SemanticQuerySqlWriter.WriteWhere(op.Filter, entity, alias, parameters, _metadata)
                    ?? throw new InvalidOperationException("Update filter produced no SQL.");
        // SQLite accepts the table without an alias here; compile the filter against the table name.
        where = where.Replace("\"t0\".", Table(entity.EffectiveStorageName) + ".", StringComparison.Ordinal);
        sb.Append(where);
        AppendReturning(sb, entity, op.ReturnFields, out var returns);
        return new SqlMutationPlan(sb.ToString(), parameters, returns);
    }

    private SqlMutationPlan CompileDelete(MutationOperation op)
    {
        if (op.Filter is null) throw new InvalidOperationException("Delete requires a filter.");
        var entity = _metadata.GetEntity(op.Entity.Id);
        var parameters = new List<SqlParameterBinding>();
        var where = SemanticQuerySqlWriter.WriteWhere(op.Filter, entity, "t0", parameters, _metadata)
                    ?? throw new InvalidOperationException("Delete filter produced no SQL.");
        where = where.Replace("\"t0\".", Table(entity.EffectiveStorageName) + ".", StringComparison.Ordinal);
        var sql = $"DELETE FROM {Table(entity.EffectiveStorageName)} WHERE {where}";
        return new SqlMutationPlan(sql, parameters, []);
    }

    private void AppendReturning(
        StringBuilder sb,
        EntityMetadata entity,
        IReadOnlyList<FieldId>? fields,
        out IReadOnlyList<MutationReturnBinding> bindings)
    {
        var requested = fields is { Count: > 0 }
            ? fields
            : entity.EffectiveFields.Where(f => f.Column is not null).Select(f => f.Id).ToArray();

        var result = new List<MutationReturnBinding>();
        sb.Append(" RETURNING ");
        for (var i = 0; i < requested.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            var field = entity.EffectiveFields.FirstOrDefault(f => f.Id == requested[i])
                        ?? throw new InvalidOperationException($"Unknown return field '{requested[i]}'.");
            var column = ResolveColumn(entity, field.Column!.ColumnId);
            var resultName = "r_" + field.Id.Value;
            sb.Append(Q(column)).Append(" AS ").Append(Q(resultName));
            result.Add(new MutationReturnBinding(field.Id, resultName));
        }

        bindings = result;
    }

    private static string ResolveColumn(EntityMetadata entity, ColumnId id) =>
        entity.Columns.FirstOrDefault(c => c.Id == id)?.EffectiveStorageName
        ?? throw new InvalidOperationException($"Column '{id.Value}' is not registered on '{entity.Name}'.");

    private static string ResolveColumn(EntityMetadata entity, FieldMetadata field) =>
        field.Column is { } reference
            ? ResolveColumn(entity, reference.ColumnId)
            : throw new InvalidOperationException($"Field '{field.Name}' has no storage column mapping.");

    private static string Q(string identifier) =>
        "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    private static string Table(string storageName) =>
        string.Join(".",
            storageName.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(Q));
}