using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.Authorization;
using Foundgine.Core.Semantic.IR;
using Foundgine.SupplyChain.Advanced.Authorization;
using Foundgine.SupplyChain.Advanced.Semantics;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton(SupplyChainSemanticModel.Build());
builder.Services.AddMcpServer()
    .WithHttpTransport(o => o.Stateless = true)
    .WithTools<SupplyChainMcpTools>();

var app = builder.Build();
app.MapMcp("/mcp");
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.Run();

public partial class Program
{
}

[McpServerToolType]
public sealed class SupplyChainMcpTools(
    SemanticModel model,
    ILogger<SupplyChainMcpTools> logger,
    IHostEnvironment environment)
{
    // docs/SECURITY.md ("Open question: the semantic API's response
    // verbosity") flagged policy_probe's Denied/RequiresClarification
    // distinction and its named-claim spoofing detail as appropriate for a
    // teaching surface but not decided for production exposure. This makes
    // that call explicit rather than leaving it open: the verbose,
    // decision-path detail is only ever returned when this host is running
    // as a development/lab environment (ASPNETCORE_ENVIRONMENT=Development
    // — unset/anything else defaults to production behavior, fail-closed).
    // Any other environment gets the same uniform,
    // correlation-id-only shape the execution API already uses (see
    // MCP.Foundgine/Program.cs's Execute/BlockedCallError), with the full
    // decision detail going only to the structured server-side log.
    private bool ExposeDiagnostics => environment.IsDevelopment();
    private static readonly IReadOnlyDictionary<string, (string TenantId, SupplyChainRole Role, string Token)> Actors =
        new Dictionary<string, (string, SupplyChainRole, string)>(StringComparer.OrdinalIgnoreCase)
        {
            ["alice"] = ("tenant-a", SupplyChainRole.Customer, "alice-demo-token"),
            ["analyst-a"] = ("tenant-a", SupplyChainRole.Analyst, "analyst-a-demo-token"),
            ["operator-a"] = ("tenant-a", SupplyChainRole.WarehouseOperator, "operator-a-demo-token"),
            ["manager-a"] = ("tenant-a", SupplyChainRole.SupplyChainManager, "manager-a-demo-token"),
            ["analyst-b"] = ("tenant-b", SupplyChainRole.Analyst, "analyst-b-demo-token")
        };

    private static (string TenantId, SupplyChainRole Role) Authenticate(string actor, string token)
    {
        if (!Actors.TryGetValue(actor, out var identity) ||
            !CryptographicEquals(identity.Token, token))
            throw new UnauthorizedAccessException("Invalid actor credentials.");

        return (identity.TenantId, identity.Role);
    }

    private static bool CryptographicEquals(string expected, string actual)
    {
        if (expected.Length != actual.Length) return false;
        var diff = 0;
        for (var i = 0; i < expected.Length; i++) diff |= expected[i] ^ actual[i];
        return diff == 0;
    }

    private ConfiguredSemanticAuthorizationPolicy Policy(string actor, string token) =>
        Policy(actor, token, ClaimsValidationResult.Empty);

    private ConfiguredSemanticAuthorizationPolicy Policy(string actor, string token,
        ClaimsValidationResult validatedClaims)
    {
        var identity = Authenticate(actor, token);
        return SupplyChainAuthorization.Create(identity.TenantId, identity.Role, validatedClaims.Accepted);
    }

    /// <summary>
    /// Validates a raw, client-supplied claim dictionary. This is the only
    /// place claims cross from "whatever the MCP caller sent" into
    /// "something a policy is allowed to consider" — see
    /// <see>
    ///     <cref>ClientClaimsValidator</cref>
    /// </see>
    /// for the fail-closed rules.
    /// Identity/tenant/role are never taken from <paramref name="claims"/>;
    /// they always come from <c>Authenticate(actor, token)</c>.
    /// </summary>
    private static ClaimsValidationResult ValidateClaims(Dictionary<string, string>? claims) =>
        ClientClaimsValidator.Validate(claims, DateTimeOffset.UtcNow);

    private static object WithClaimDiagnostics(object payload, ClaimsValidationResult result) => new
    {
        result = payload,
        acceptedClaims = result.Accepted,
        rejectedClaims = result.Rejected.Select(r => new { r.Key, r.Value, r.Reason }).ToArray()
    };

    [McpServerTool(Name = "describe_capabilities")]
    public object DescribeCapabilities(string actor, string token) =>
        SemanticAuthorizationCapabilityDiscovery.Describe(model, Policy(actor, token));

    [McpServerTool(Name = "read_entity")]
    public object ReadEntity(string actor, string token, string entity, string[] fields,
        Dictionary<string, string>? claims = null)
    {
        var validatedClaims = ValidateClaims(claims);
        if (validatedClaims.IsSpoofingAttempt)
            return ClaimSpoofingError(validatedClaims);

        var entityDefinition = model.Entities.SingleOrDefault(x =>
            x.Name.Equals(entity, StringComparison.OrdinalIgnoreCase));
        if (entityDefinition is null) return Error("Unknown semantic entity.");

        var selectedFields = entityDefinition.Fields
            .Where(x => fields.Contains(x.Name, StringComparer.OrdinalIgnoreCase))
            .Select(x => x.Id)
            .ToArray();

        var graph = new SemanticGraph();
        graph.AddRoot(entityDefinition.Id, selectedFields);
        var operation = SemanticOperationCompiler.Compile(graph);
        var authorized = new SemanticAuthorizer(Policy(actor, token, validatedClaims)).Authorize(operation);

        return WithClaimDiagnostics(new
        {
            allowed = true,
            entity = entityDefinition.Name,
            fields = authorized.Root.Fields.Select(id => entityDefinition.Fields.First(f => f.Id == id).Name).ToArray(),
            predicate = authorized.Root.Authorization
        }, validatedClaims);
    }

    [McpServerTool(Name = "read_relationship")]
    public object ReadRelationship(string actor, string token, string entity, string relationship)
    {
        var source = model.Entities.SingleOrDefault(x => x.Name.Equals(entity, StringComparison.OrdinalIgnoreCase));
        if (source is null) return Error("Unknown semantic entity.");
        var relation =
            source.Relationships.SingleOrDefault(x => x.Name.Equals(relationship, StringComparison.OrdinalIgnoreCase));
        if (relation is null) return Error("Unknown semantic relationship.");

        var graph = new SemanticGraph();
        var root = graph.AddRoot(source.Id, source.Fields.Select(x => x.Id));
        graph.Add(relation.Target, relation.Id, root, []);
        var operation = SemanticOperationCompiler.Compile(graph);
        var authorized = new SemanticAuthorizer(Policy(actor, token)).Authorize(operation);

        return new
        {
            allowed = true,
            entity = source.Name,
            relationship = relation.Name,
            children = authorized.Root.Children.Select(x => model.Get(x.EntityId).Name).ToArray()
        };
    }

    [McpServerTool(Name = "write_entity")]
    public object WriteEntity(string actor, string token, string entity, string operation = "update",
        Dictionary<string, string>? claims = null)
    {
        var validatedClaims = ValidateClaims(claims);
        if (validatedClaims.IsSpoofingAttempt)
            return ClaimSpoofingError(validatedClaims);

        var definition = model.Entities.SingleOrDefault(x => x.Name.Equals(entity, StringComparison.OrdinalIgnoreCase));
        if (definition is null) return Error("Unknown semantic entity.");

        var policy = Policy(actor, token, validatedClaims);
        var decision = policy.GetEntityAccess(
            definition.Id,
            AuthorizationOperation.Write,
            new AuthorizationOperationName(operation));

        return decision.IsAllowed
            ? WithClaimDiagnostics(new { allowed = true, entity = definition.Name, operation }, validatedClaims)
            : WithClaimDiagnostics(Error("Semantic authorization denied the write operation."), validatedClaims);
    }

    [McpServerTool(Name = "policy_probe")]
    public object PolicyProbe(string actor, string token, string attack, Dictionary<string, string>? claims = null)
    {
        var validatedClaims = ValidateClaims(claims);
        if (validatedClaims.IsSpoofingAttempt)
            return ClaimSpoofingError(validatedClaims);

        var policy = Policy(actor, token, validatedClaims);
        object? result = attack switch
        {
            "cross-tenant" => policy.GetPredicate(SupplyChainSemanticModel.Warehouse, AuthorizationOperation.Read),
            "sensitive-field" => policy.GetFieldAccess(SupplyChainSemanticModel.InventoryLot,
                SupplyChainAuthorization.FieldIds.InventoryQuarantined, AuthorizationOperation.Read),
            "relationship-escalation" => policy.GetRelationshipAccess(SupplyChainSemanticModel.Supplier,
                SupplyChainAuthorization.RelationshipIds.SupplierIncidents, AuthorizationOperation.Read),
            "write-escalation" => policy.GetEntityAccess(SupplyChainSemanticModel.InventoryLot,
                AuthorizationOperation.Write),
            "named-operation" => policy.GetEntityAccess(SupplyChainSemanticModel.InventoryLot,
                AuthorizationOperation.Write, new AuthorizationOperationName("inventory.reconcile")),
            // Claims-driven probes. All of these route through the same
            // Authenticate(actor, token) identity as every other probe; only
            // the claim set changes. See GUIDE.md "Claims validation" for the
            // full attack/legitimate-use matrix these correspond to.
            "claims-scope-narrowing" => policy.GetEntityAccess(SupplyChainSemanticModel.InventoryLot,
                AuthorizationOperation.Write),
            "claims-warehouse-scoping" => policy.GetPredicate(SupplyChainSemanticModel.Warehouse,
                AuthorizationOperation.Read),
            "claims-reconcile" => policy.GetEntityAccess(SupplyChainSemanticModel.InventoryLot,
                AuthorizationOperation.Write, new AuthorizationOperationName("inventory.reconcile")),
            _ => null
        };

        object body = result switch
        {
            AuthorizationPredicate predicate => new { allowed = false, kind = "conditional", predicate },
            AuthorizationDecision decision => new { allowed = decision.IsAllowed, kind = decision.Access.ToString() },
            _ => Error("Unknown policy probe.")
        };

        var diagnostics = WithClaimDiagnostics(body, validatedClaims);

        if (ExposeDiagnostics)
            return diagnostics;

        // Production path: the decision-path detail (Denied vs.
        // RequiresClarification vs. a named rejected claim) is exactly what
        // a caller probing many `attack` variants could use to map policy
        // boundaries faster than a uniform response would allow, per
        // docs/SECURITY.md. Log the full decision server-side, keyed by a
        // short opaque correlation id, and return only that id to the
        // caller.
        var correlationId = Guid.NewGuid().ToString("N")[..8];
        logger.LogInformation(
            "policy_probe decision. correlationId={CorrelationId} actor={Actor} attack={Attack} decision={Decision}",
            correlationId, actor, attack, diagnostics);

        return new { allowed = false, reference = correlationId };
    }

    /// <summary>
    /// A raw claim set tried to assert identity or privilege directly
    /// (role, tenant, admin flags, ...). This is rejected before
    /// authentication's own tenant/role is even consulted, and the whole
    /// call fails closed — see <see>
    ///     <cref>ClientClaimsValidator</cref>
    /// </see>
    /// .
    /// </summary>
    private static object ClaimSpoofingError(ClaimsValidationResult result) => new
    {
        isError = true,
        message =
            "Rejected: claims attempted to assert identity or privilege directly. Identity comes only from actor/token authentication.",
        rejectedClaims = result.Rejected.Select(r => new { r.Key, r.Value, r.Reason }).ToArray()
    };

    private static object Error(string message) => new { isError = true, message };
}