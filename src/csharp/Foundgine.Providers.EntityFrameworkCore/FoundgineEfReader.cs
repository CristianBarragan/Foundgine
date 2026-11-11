using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Foundgine.Core.Abstractions;
using Foundgine.Core.Execution;
using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.Authorization;
using Foundgine.Core.Semantic.IR;
using PlanningSemantic = Foundgine.Core.Semantic.Planning;
using Foundgine.Core.Semantic.Query;
using ExecutionCore = Foundgine.Core.Execution;
using ExecutionContext = Foundgine.Core.Execution.ExecutionContext;

namespace Foundgine.Providers.EntityFrameworkCore;

/// <summary>Rows of a semantic read plus the fingerprint of the plan that produced them.</summary>
public sealed record FoundgineReadResult(IReadOnlyList<ExecutionRow> Rows, string Fingerprint);

/// <summary>
/// The semantic read path on EF Core: semantic operation -> authorization -> plan -> ExecutionIR -> EF plan ->
/// EF execution (no tracking). One method for the common case, the full pipeline underneath for anything else.
/// </summary>
public sealed class FoundgineEfReader(
    FoundgineDbContext db, PlanningSemantic.Planner planner, SemanticAuthorizer authorizer,
    SemanticContractSnapshot contract, EfCorePlanCompiler compiler)
{
    public async Task<FoundgineReadResult> ReadAsync(SemanticOperation operation, CancellationToken ct = default,
        ExecutionContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var plan = planner.Plan(contract, authorizer.AuthorizeWithEvidence(contract, operation));
        var ir = ExecutionIRCompiler.Compile(plan);
        var result = await new EfCoreExecutionProvider(db, db.FoundgineModel.Registry)
            .ExecuteAsync(compiler.Compile(ir), context ?? new ExecutionContext(), ct);
        var json = JsonSerializer.SerializeToUtf8Bytes(
            new PlanFingerprintPayload(plan.Root, ir.Root),
            FoundgineReaderJsonContext.Default.PlanFingerprintPayload);
        var fingerprint = Convert.ToHexStringLower(SHA256.HashData(json))[..24];
        return new FoundgineReadResult(result.Rows, fingerprint);
    }

    /// <summary>Single-entity read: fields, optional filter and ordering.</summary>
    public Task<FoundgineReadResult> ReadAsync(EntityId entity, IReadOnlyList<FieldId> fields,
        SemanticFilterExpression? filter = null, IReadOnlyList<SemanticOrderTerm>? order = null,
        CancellationToken ct = default) =>
        ReadAsync(Operation(entity, fields, filter, order), ct);

    /// <summary>Executes a fluent <see cref="SemanticQuery"/>.</summary>
    public Task<FoundgineReadResult> ReadAsync(SemanticQuery query, CancellationToken ct = default) =>
        ReadAsync(query.Build(), ct);

    /// <summary>Starts a fluent query over this reader's metadata: <c>reader.ReadAsync(reader.From(e).Where(..))</c>.</summary>
    public SemanticQuery From(EntityId entity) => SemanticQuery.From(db.FoundgineModel.Metadata, entity);

    /// <summary>Lambda form: <c>reader.ReadAsync(entity, q => q.Where(..).Include(..), ct)</c>.</summary>
    public Task<FoundgineReadResult> ReadAsync(EntityId entity, Func<SemanticQuery, SemanticQuery> configure,
        CancellationToken ct = default) => ReadAsync(configure(From(entity)), ct);

    /// <summary>Builds a single-node read operation (also usable with relationship children via the core IR types).</summary>
    public static SemanticOperation Operation(EntityId entity, IReadOnlyList<FieldId> fields,
        SemanticFilterExpression? filter = null, IReadOnlyList<SemanticOrderTerm>? order = null) =>
        new(new SemanticReadNode(1, entity, fields, null, null, [], new SemanticQueryOptions(filter, order ?? [])));
}

internal readonly record struct PlanFingerprintPayload(
    [property: JsonPropertyName("semantic")] PlanningSemantic.SemanticPlanNode Semantic,
    [property: JsonPropertyName("execution")] ExecutionCore.ExecutionIRNode Execution);

[JsonSerializable(typeof(PlanFingerprintPayload))]
internal partial class FoundgineReaderJsonContext : JsonSerializerContext;
