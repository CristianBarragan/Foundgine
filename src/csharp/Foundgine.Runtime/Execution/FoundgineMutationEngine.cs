using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Foundgine.Core.Abstractions;
using Foundgine.Core.Execution;
using Foundgine.Core.Execution.Mutation;
using Foundgine.Core.Semantic.Planning.Mutation;
using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.Authorization;
using Foundgine.Core.Semantic.Capabilities;
using Foundgine.Core.Semantic.Security;
using Foundgine.Core.Semantic.Security.Execution;
using Foundgine.Core.Semantic.Security.Warrants;
using Foundgine.Core.Semantic.Mutation;
using Foundgine.Core.Semantic.Query;
using ExecutionContext = Foundgine.Core.Execution.ExecutionContext;

namespace Foundgine.Runtime;

public sealed class FoundgineMutationEngine : IFoundgineMutations
{
    private readonly IMutationSchema _schema;
    private readonly SemanticModel? _model;
    private readonly ISemanticAuthorizationPolicy _policy;
    private readonly IMutationBatchExecutionProvider _provider;
    private readonly SemanticCapabilityContract _securityContract;
    private readonly ISecurityWarrantKeyResolver? _warrantKeyResolver;
    private readonly string? _expectedWarrantIssuer;
    private readonly ISecurityWarrantReplayStore? _warrantReplayStore;
    private readonly ISecurityWarrantRevocationStore? _warrantRevocationStore;
    private readonly ISecurityWarrantDelegationTrustResolver? _warrantDelegationTrustResolver;
    private readonly SecurityResourceLimits _securityResourceLimits;

    public FoundgineMutationEngine(
        IMutationSchema schema,
        ISemanticAuthorizationPolicy policy,
        IMutationBatchExecutionProvider provider,
        SemanticModel? model = null,
        ISecurityWarrantKeyResolver? warrantKeyResolver = null,
        string? expectedWarrantIssuer = null,
        ISecurityWarrantReplayStore? warrantReplayStore = null,
        SecurityResourceLimits? securityResourceLimits = null,
        ISecurityWarrantRevocationStore? warrantRevocationStore = null,
        ISecurityWarrantDelegationTrustResolver? warrantDelegationTrustResolver = null)
    {
        _schema = schema ?? throw new ArgumentNullException(nameof(schema));
        _model = model;
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _securityContract = model is null
            ? new SemanticCapabilityContract(1, [])
            : SemanticCapabilityContractDiscovery.Describe(model, policy);
        SecurityInvariantContractValidator.EnsureContractValid(_securityContract);
        _warrantKeyResolver = warrantKeyResolver;
        _expectedWarrantIssuer = expectedWarrantIssuer;
        _warrantReplayStore = warrantReplayStore;
        _warrantRevocationStore = warrantRevocationStore;
        _warrantDelegationTrustResolver = warrantDelegationTrustResolver;
        _securityResourceLimits = securityResourceLimits ?? new SecurityResourceLimits();
        _securityResourceLimits.Validate();
    }

    public MutationDryRunResult DryRun(SemanticMutationRequest request)
    {
        var plan = AuthorizeAndPlan(request);
        return Describe(plan);
    }

    /// <summary>
    /// Executes a mutation directly after authorization, security-invariant
    /// validation and final execution certification. This is the normal path
    /// for trusted transports such as GraphQL; approval remains an explicit
    /// optional workflow rather than an accidental requirement for every mutation.
    /// </summary>
    public Task<MutationExecutionResult> ExecuteAsync(
        SemanticMutationRequest request,
        ExecutionContext? context = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var plan = AuthorizeAndPlan(request);
        return ExecutePlanAsync(plan, request.Security, approval: null, context, cancellationToken);
    }

    public MutationPlanApproval Approve(SemanticMutationRequest request, string approvedBy)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(approvedBy);
        var dryRun = DryRun(request);
        return new MutationPlanApproval(
            request,
            Guid.NewGuid().ToString("N"),
            dryRun.PlanFingerprint,
            approvedBy,
            DateTimeOffset.UtcNow);
    }

    public Task<MutationExecutionResult> ExecuteApprovedAsync(
        MutationPlanApproval approval,
        ExecutionContext? context = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(approval);
        cancellationToken.ThrowIfCancellationRequested();

        var plan = AuthorizeAndPlan(approval.Request);
        var fingerprint = Fingerprint(plan);
        if (!string.Equals(fingerprint, approval.PlanFingerprint, StringComparison.Ordinal))
            throw new InvalidOperationException(
                "The approved mutation plan changed after approval. Re-run dry-run and obtain a new approval.");

        return ExecutePlanAsync(plan, approval.Request.Security, approval, context, cancellationToken);
    }

    private Task<MutationExecutionResult> ExecutePlanAsync(
        SemanticMutationPlan plan,
        SecurityExecutionContext? security,
        MutationPlanApproval? approval,
        ExecutionContext? context,
        CancellationToken cancellationToken)
    {
        var ir = new SemanticMutationExecutionLowerer(_schema).Lower(plan);
        var executionContext = context ?? new ExecutionContext();
        executionContext.EnsureWithinDeadline();
        using var deadlineCts = executionContext.CreateDeadlineCancellationSource(cancellationToken);

        var certificate = MutationExecutionSecurityGate.Certify(
            ir,
            _provider,
            _provider.GetType().FullName ?? _provider.GetType().Name,
            plan.RequiredSecurityInvariants.Where(IsEnginePreservedInvariant));

        // The final gate binds the exact lowered IR to the provider and required
        // invariants immediately before any side effect. GraphQL therefore cannot
        // bypass the same mutation security boundary used by MCP/direct callers.
        MutationExecutionSecurityGate.EnsureExecutable(ir, _provider, certificate);

        // Revalidate the complete warrant trust and revocation boundary as late
        // as possible: authorization and IR certification may have taken time,
        // and a warrant can be revoked while those checks are running.
        SecurityWarrantRevocationSnapshot? revocationSnapshot = null;
        if (security is not null)
        {
            var dispatchTime = DateTimeOffset.UtcNow;
            VerifyWarrantTrust(security, dispatchTime);
            revocationSnapshot = ValidateWarrantRevocation(security, dispatchTime);

            // A warrant is single-use. Consume it only after all checks, including
            // the final trust/revocation check, have passed.
            ConsumeWarrantReplay(security);
        }

        var result = _provider.ExecuteBatch(ir, executionContext, deadlineCts.Token);
        executionContext.EnsureWithinDeadline();
        if (security is not null && revocationSnapshot is { } snapshot)
        {
            snapshot.AssertUnchanged(_warrantRevocationStore!);
            ValidateWarrantRevocation(security, DateTimeOffset.UtcNow);
        }
        var resultFingerprint = FingerprintResult(result);
        return Task.FromResult(new MutationExecutionResult(
            result,
            Fingerprint(plan),
            resultFingerprint,
            approval?.ApprovalId,
            approval?.ApprovedBy));
    }

    private void ValidateWarrant(SemanticMutationRequest request)
    {
        var security = request.Security;
        if (security is null)
            return;

        if (_warrantKeyResolver is null)
            throw new InvalidOperationException(
                "A security warrant was supplied, but no warrant key resolver is configured.");

        var now = DateTimeOffset.UtcNow;
        VerifyWarrantTrust(security, now);
        ValidateWarrantRevocation(security, now);

        if (!StringComparer.Ordinal.Equals(security.Warrant.Subject, security.Subject))
            throw new UnauthorizedAccessException(
                "The warrant does not authorize the requested subject.");

        var allowedTenants = security.Warrant.Constraints.AllowedTenants;
        if (allowedTenants.Count > 0 &&
            (security.Tenant is null || !allowedTenants.Contains(security.Tenant, StringComparer.Ordinal)))
            throw new UnauthorizedAccessException(
                "The warrant does not authorize the requested tenant scope.");

        foreach (var operation in request.Graph.Operations)
        {
            var capability = _securityContract.Capabilities.FirstOrDefault(c =>
                c.TargetEntityId == operation.Entity &&
                string.Equals(c.Operation, operation.Kind.ToString().ToLowerInvariant(), StringComparison.Ordinal));

            if (capability is null)
                throw new UnauthorizedAccessException(
                    $"No security capability contract exists for entity '{operation.Entity}' and operation '{operation.Kind}'.");

            if (!SecurityWarrantAuthorization.Allows(
                    security.Warrant,
                    security.Subject,
                    security.Audience,
                    capability.Id,
                    capability.Operation,
                    security.Tenant,
                    security.ResourceScope))
            {
                throw new UnauthorizedAccessException(
                    $"Security warrant does not authorize capability '{capability.Id}' for subject '{security.Subject}'.");
            }

            ValidateWarrantAllowedFields(operation, security);
        }
    }



    private void VerifyWarrantTrust(SecurityExecutionContext security, DateTimeOffset now)
    {
        var warrant = security.Warrant;
        var isDelegated = warrant.ParentId is not null || warrant.ParentDigest is not null ||
                          warrant.DelegationPath.Count != 0;

        if (isDelegated || security.DelegationChain is not null)
        {
            SecurityWarrantExecutionTrust.Verify(
                warrant,
                _warrantKeyResolver!,
                _expectedWarrantIssuer ?? throw new InvalidOperationException(
                    "Delegated warrant execution requires an explicitly configured root issuer."),
                security.Audience,
                now,
                security.DelegationChain,
                _warrantDelegationTrustResolver,
                security.Tenant);
            return;
        }

        SecurityWarrantVerifier.Verify(
            warrant, _warrantKeyResolver!, now, _expectedWarrantIssuer, security.Audience);
    }

    private SecurityWarrantRevocationSnapshot ValidateWarrantRevocation(
        SecurityExecutionContext security,
        DateTimeOffset now)
    {
        if (_warrantRevocationStore is null)
            throw new InvalidOperationException(
                "Warrant-backed mutation discovery and execution require a warrant revocation store; refusing to run without current revocation state.");

        return SecurityWarrantRevocationGuard.Validate(
            security.Warrant, _warrantRevocationStore, now);
    }

    private void ValidateWarrantAllowedFields(
        SemanticMutationOperation operation,
        SecurityExecutionContext security)
    {
        var allowed = security.Warrant.Constraints.AllowedFields;
        if (allowed.Count == 0)
            return;

        if (_model is null)
            throw new InvalidOperationException(
                "A mutation warrant specifies allowed fields, but no SemanticModel is configured to resolve them.");

        var entity = _model.Get(operation.Entity);
        var requested = operation.Fields
            .Where(field => field.Source is null)
            .Select(field => entity.Fields.FirstOrDefault(x => x.Id == field.Field))
            .Where(field => field is not null)
            .Select(field => field!.Name)
            .Concat(operation.ReturnFields
                .Select(field => entity.Fields.FirstOrDefault(x => x.Id == field))
                .Where(field => field is not null)
                .Select(field => field!.Name))
            .Concat(FilterFields(operation.Filter, entity))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (requested.Any(field => !allowed.Contains(field, StringComparer.Ordinal)))
            throw new UnauthorizedAccessException(
                $"Security warrant does not authorize one or more requested mutation fields on '{entity.Name}'.");
    }

    private static IEnumerable<string> FilterFields(
        SemanticFilterExpression? filter,
        SemanticEntity entity)
    {
        switch (filter)
        {
            case SemanticFieldFilter field:
                var semanticField = entity.Fields.FirstOrDefault(x => x.Id == field.Field);
                if (semanticField is not null)
                    yield return semanticField.Name;
                yield break;

            case SemanticAndFilter and:
                foreach (var expression in and.Expressions)
                foreach (var name in FilterFields(expression, entity))
                    yield return name;
                yield break;

            case SemanticOrFilter or:
                foreach (var expression in or.Expressions)
                foreach (var name in FilterFields(expression, entity))
                    yield return name;
                yield break;

            default:
                yield break;
        }
    }

    private void ConsumeWarrantReplay(SecurityExecutionContext security)
    {
        if (_warrantReplayStore is null)
            throw new InvalidOperationException("Executing a warrant-backed mutation requires a warrant replay store.");

        SecurityWarrantReplayGuard.Consume(
            security.Warrant,
            _warrantReplayStore,
            DateTimeOffset.UtcNow);
    }

    private SemanticMutationPlan AuthorizeAndPlan(SemanticMutationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        MutationSecurityResourceLimitValidator.Validate(request, _securityResourceLimits);
        ValidateWarrant(request);
        var semanticPlan = new SemanticMutationPlanner().Plan(request.Graph);
        var authorizedPlan = new MutationAuthorizer(_schema, _policy).Authorize(semanticPlan);
        var requiredSecurityInvariants = RequiredSecurityInvariantsFor(authorizedPlan);

        // Authorization is applied to the exact semantic representation that is
        // subsequently lowered into ExecutionMutationIR. No independently planned
        // batch is discarded or allowed to become an alternate execution source.
        return authorizedPlan with
        {
            RequiredSecurityInvariants = requiredSecurityInvariants
        };
    }

    private IReadOnlyList<string> RequiredSecurityInvariantsFor(SemanticMutationPlan plan)
    {
        var required = new HashSet<string>(StringComparer.Ordinal);

        foreach (var operation in plan.Operations)
        {
            var capability = _securityContract.Capabilities.FirstOrDefault(c =>
                c.TargetEntityId == operation.Entity &&
                string.Equals(c.Operation, operation.Kind.ToString().ToLowerInvariant(), StringComparison.Ordinal));

            if (capability is null)
                throw new InvalidOperationException(
                    $"No security capability contract exists for entity '{operation.Entity}' and operation '{operation.Kind}'.");

            foreach (var invariant in capability.EffectiveSecurityInvariants)
                required.Add(invariant);
        }

        if (required.Count == 0)
            throw new InvalidOperationException(
                "A mutation execution cannot proceed without explicit security invariants.");

        return required.OrderBy(x => x, StringComparer.Ordinal).ToArray();
    }

    private static bool IsEnginePreservedInvariant(string id) => id switch
    {
        SecurityInvariantIds.AuthorizationRequired => true,
        SecurityInvariantIds.RuntimeAuthorization => true,
        SecurityInvariantIds.FieldVisibility => true,
        SecurityInvariantIds.RelationshipVisibility => true,
        _ => false
    };

    private MutationDryRunResult Describe(SemanticMutationPlan plan) =>
        new(
            Fingerprint(plan),
            plan.Operations.Select((x, i) => new MutationPlanOperation(
                i,
                _schema.GetEntity(x.Entity).Name,
                x.Kind.ToString(),
                x.Fields.Select(f => f.Field.Value.ToString()).ToArray(),
                x.ReturnFields.Select(f => f.Value.ToString()).ToArray())).ToArray(),
            plan.Operations.SelectMany(x => x.Effects).Select(FormatEffect).ToArray());

    private string FormatEffect(SemanticMutationEffect effect) =>
        $"{effect.Kind}:{_schema.GetEntity(effect.Entity).Name}" +
        (effect.Field is { } field ? $".{field.Value}" : string.Empty);

    private static string Fingerprint(SemanticMutationPlan plan)
    {
        var canonical = JsonSerializer.Serialize(new
        {
            version = "mutation-plan-v1",
            operations = plan.Operations,
            dependencies = plan.Dependencies,
            security = plan.RequiredSecurityInvariants
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static string FingerprintResult(MutationBatchResult result)
    {
        var canonical = JsonSerializer.Serialize(result.Results.Select(x => new
        {
            x.AffectedRows,
            returned = x.ReturnedValues?.OrderBy(p => p.Key.Value)
                .Select(p => new { field = p.Key.Value, value = p.Value })
        }));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
