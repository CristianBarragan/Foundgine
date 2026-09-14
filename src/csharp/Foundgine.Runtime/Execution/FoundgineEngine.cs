using Foundgine.Core.Execution;
using Foundgine.Core.Semantic.Planning;
using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.Authorization;
using Foundgine.Core.Semantic.Capabilities;
using Foundgine.Core.Semantic.IR;
using Foundgine.Core.Semantic.Resolution;
using Foundgine.Core.Semantic.Security;
using Foundgine.Core.Semantic.Security.Execution;
using Foundgine.Core.Semantic.Security.Warrants;
using System.Text.Json;
using ExecutionContext = Foundgine.Core.Execution.ExecutionContext;

namespace Foundgine.Runtime;

/// <summary>
/// Application-facing facade over the Foundgine semantic execution pipeline.
/// Applications normally obtain this through dependency injection.
/// </summary>
public sealed class FoundgineEngine : IFoundgine
{
    private readonly SemanticModel _model;
    private readonly SemanticContractSnapshot _contract;
    private readonly ISemanticAuthorizationPolicy _authorizationPolicy;
    private readonly IPlanner _planner;
    private readonly IPlanOptimizer _planOptimizer;
    private readonly IProviderPlanCompiler _compiler;
    private readonly IExecutionProvider _provider;
    private readonly IProviderPlanCache _planCache;
    private readonly SemanticVersionSet _versions;
    private readonly SemanticCapabilityContract _securityContract;
    private readonly ISecurityWarrantKeyResolver? _warrantKeyResolver;
    private readonly string? _expectedWarrantIssuer;
    private readonly ISecurityWarrantReplayStore? _warrantReplayStore;
    private readonly SecurityResourceLimits _securityResourceLimits;
    private readonly IExecutionAuthorizationRevalidator _executionAuthorizationRevalidator;

    private readonly
        Func<SemanticAuthorizationEvidence, CancellationToken, ValueTask<ExecutionAuthorizationAuthorityState?>>?
        _executionAuthorizationAuthorityResolver;

    private readonly string _cacheNamespace = Guid.NewGuid().ToString("N");

    internal FoundgineEngine(
        FoundgineOptions options,
        SemanticContractSnapshot contract,
        IProviderPlanCompiler compiler,
        IExecutionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(options);
        _model = options.Model ?? throw new InvalidOperationException(
            "FoundgineOptions.Model must be configured.");
        _contract = contract ?? throw new ArgumentNullException(nameof(contract));
        _authorizationPolicy = options.AuthorizationPolicy ?? throw new InvalidOperationException(
            "FoundgineOptions.AuthorizationPolicy must be configured.");
        _planner = new Planner();
        _planOptimizer = new SemanticPlanOptimizer();
        _compiler = compiler ?? throw new ArgumentNullException(nameof(compiler));
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _planCache = options.PlanCache ?? new MemoryProviderPlanCache();
        _versions = SemanticVersionSet.For(_model);
        _securityContract = SemanticCapabilityContractDiscovery.Describe(_model, _authorizationPolicy);
        SecurityInvariantContractValidator.EnsureContractValid(_securityContract);
        _warrantKeyResolver = options.WarrantKeyResolver;
        _expectedWarrantIssuer = options.ExpectedWarrantIssuer;
        _warrantReplayStore = options.WarrantReplayStore;
        _securityResourceLimits = options.SecurityResourceLimits ?? new SecurityResourceLimits();
        _executionAuthorizationRevalidator = options.ExecutionAuthorizationRevalidator ??
                                             new SemanticExecutionAuthorizationRevalidator();
        _executionAuthorizationAuthorityResolver = options.ExecutionAuthorizationAuthorityResolver;
        _securityResourceLimits.Validate();
    }

    /// <summary>
    /// Convenience overload for callers (largely tests) that build
    /// <see cref="FoundgineOptions"/> directly and do not already hold a
    /// frozen <see cref="SemanticContractSnapshot"/>. The snapshot is derived
    /// from <see cref="FoundgineOptions.Model"/> the same way
    /// <see cref="FoundgineServiceCollectionExtensions"/> does at startup.
    /// </summary>
    internal FoundgineEngine(
        FoundgineOptions options,
        IProviderPlanCompiler compiler,
        IExecutionProvider provider)
        : this(options, CreateContract(options), compiler, provider)
    {
    }

    private static SemanticContractSnapshot CreateContract(FoundgineOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var model = options.Model ?? throw new InvalidOperationException(
            "FoundgineOptions.Model must be configured.");
        return model.Freeze().CreateSnapshot();
    }

    /// <summary>
    /// Internal-compatible constructor for adapters/tests that intentionally
    /// provide the orchestration components themselves.
    /// </summary>
    internal FoundgineEngine(
        SemanticModel model,
        ISemanticAuthorizationPolicy authorizationPolicy,
        IPlanner planner,
        IProviderPlanCompiler compiler,
        IExecutionProvider provider,
        IProviderPlanCache? planCache = null)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _contract = _model.Freeze().CreateSnapshot();
        _authorizationPolicy = authorizationPolicy ?? throw new ArgumentNullException(nameof(authorizationPolicy));
        _planner = planner ?? throw new ArgumentNullException(nameof(planner));
        _planOptimizer = new SemanticPlanOptimizer();
        _compiler = compiler ?? throw new ArgumentNullException(nameof(compiler));
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _planCache = planCache ?? new MemoryProviderPlanCache();
        _versions = SemanticVersionSet.For(_model);
        _securityContract = SemanticCapabilityContractDiscovery.Describe(_model, _authorizationPolicy);
        SecurityInvariantContractValidator.EnsureContractValid(_securityContract);
        _securityResourceLimits = new SecurityResourceLimits();
        _executionAuthorizationRevalidator = new SemanticExecutionAuthorizationRevalidator();
    }

    public SemanticAuthorizationCapabilities DescribeCapabilities() =>
        SemanticAuthorizationCapabilityDiscovery.Describe(_model, _authorizationPolicy);

    public SemanticCapabilityContract DescribeCapabilityContract() =>
        SemanticCapabilityContractDiscovery.Describe(_model, _authorizationPolicy);

    public SemanticCapabilityContract DescribeCapabilityContract(SecurityExecutionContext security)
    {
        ArgumentNullException.ThrowIfNull(security);
        ValidateDiscoveryWarrant(security);

        var contract = SemanticCapabilityContractDiscovery.Describe(_model, _authorizationPolicy);
        var visible = contract.Capabilities
            .Where(capability => SecurityWarrantAuthorization.Allows(
                security.Warrant,
                security.Subject,
                security.Audience,
                capability.Id,
                capability.Operation,
                security.Tenant,
                security.ResourceScope))
            .ToArray();

        return contract with { Capabilities = visible };
    }

    private void ValidateDiscoveryWarrant(SecurityExecutionContext security)
    {
        if (_warrantKeyResolver is null)
            throw new InvalidOperationException(
                "Warrant-backed capability discovery requires a warrant key resolver.");

        SecurityWarrantVerifier.Verify(
            security.Warrant,
            _warrantKeyResolver,
            DateTimeOffset.UtcNow,
            _expectedWarrantIssuer,
            security.Audience);
    }

    public SemanticVersionSet DescribeVersionSet() => _versions;

    public DryRunResult DryRun(
        SemanticRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        SecurityResourceLimitValidator.Validate(request, _securityResourceLimits);

        var graph = new SemanticRequestResolver(_contract).Resolve(request);
        var semanticOperation = SemanticOperationCompiler.Compile(graph);
        ValidateWarrant(request, semanticOperation, consumeReplay: false);
        var authorization =
            new SemanticAuthorizer(_authorizationPolicy).AuthorizeWithEvidence(_contract, semanticOperation);
        authorization.EnsureMatches(_contract);
        var authorizedOperation = authorization.Operation;
        var plan = BuildSecuredPlan(authorization);
        return new DryRunResult(PlanInspector.Inspect(plan));
    }

    public PlanApproval ApprovePlan(SemanticRequest request, string approvedBy)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(approvedBy);

        var dryRun = DryRun(request);
        return new PlanApproval(
            request,
            Guid.NewGuid().ToString("N"),
            dryRun.Inspection.PlanFingerprint,
            _versions.SemanticModelVersion,
            _versions.CapabilityContractVersion,
            _versions.CapabilityVersion,
            _versions.IntentVersion,
            _versions.PlanVersion,
            approvedBy,
            DateTimeOffset.UtcNow);
    }

    public Task<ExecutionResult> ExecuteApprovedAsync(
        PlanApproval approval,
        ExecutionContext? context = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(approval);
        SecurityResourceLimitValidator.Validate(approval.Request, _securityResourceLimits);

        if (!string.Equals(approval.SemanticModelVersion, _versions.SemanticModelVersion, StringComparison.Ordinal) ||
            approval.CapabilityContractVersion != _versions.CapabilityContractVersion ||
            approval.CapabilityVersion != _versions.CapabilityVersion ||
            approval.IntentVersion != _versions.IntentVersion ||
            approval.PlanVersion != _versions.PlanVersion)
        {
            throw new InvalidOperationException(
                "The approval was created against an incompatible semantic version set. Re-run dry-run and obtain a new approval.");
        }

        var graph = new SemanticRequestResolver(_contract).Resolve(approval.Request);
        var semanticOperation = SemanticOperationCompiler.Compile(graph);
        ValidateWarrant(approval.Request, semanticOperation, consumeReplay: true);
        var authorization =
            new SemanticAuthorizer(_authorizationPolicy).AuthorizeWithEvidence(_contract, semanticOperation);
        authorization.EnsureMatches(_contract);
        var authorizedOperation = authorization.Operation;
        var plan = BuildSecuredPlan(authorization);
        var currentFingerprint = SemanticPlanFingerprint.Create(plan);

        if (!string.Equals(currentFingerprint, approval.PlanFingerprint, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The approved plan no longer matches the current authorized plan. Re-run dry-run and obtain a new approval.");
        }

        var executionIr = ExecutionIRCompiler.Compile(plan);
        var cacheKey = BuildProviderPlanCacheKey(plan, approval.Request.Security);
        var providerPlan = _planCache.GetOrAdd(
            cacheKey,
            () => SecurityInvariantProofGate.AttachAndValidate(
                _compiler.Compile(executionIr), executionIr, _compiler));

        var executionContext = AttachPaginationContext(plan, context ?? new ExecutionContext());
        return ExecuteAndEnrichEvidenceAsync(
            approval.Request,
            plan,
            providerPlan,
            executionContext,
            executionIr,
            cancellationToken,
            approval,
            authorization.Evidence);
    }

    public Task<ExecutionResult> ExecuteAsync(
        Foundgine.Core.Semantic.Intent.ReadIntent intent,
        ExecutionContext? context = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);
        var request = new Foundgine.Core.Semantic.Intent.ReadIntentCompiler(_model).Compile(intent);
        return ExecuteAsync(request, context, cancellationToken);
    }

    public Task<ExecutionResult> ExecuteAsync(
        SemanticRequest request,
        ExecutionContext? context = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        SecurityResourceLimitValidator.Validate(request, _securityResourceLimits);

        var graph = new SemanticRequestResolver(_contract).Resolve(request);
        var semanticOperation = SemanticOperationCompiler.Compile(graph);
        ValidateWarrant(request, semanticOperation, consumeReplay: true);
        var authorization =
            new SemanticAuthorizer(_authorizationPolicy).AuthorizeWithEvidence(_contract, semanticOperation);
        authorization.EnsureMatches(_contract);
        var authorizedOperation = authorization.Operation;
        var plan = BuildSecuredPlan(authorization);
        var executionIr = ExecutionIRCompiler.Compile(plan);
        var cacheKey = BuildProviderPlanCacheKey(plan, request.Security);
        var providerPlan = _planCache.GetOrAdd(
            cacheKey,
            () => SecurityInvariantProofGate.AttachAndValidate(
                _compiler.Compile(executionIr), executionIr, _compiler));

        var executionContext = AttachPaginationContext(plan, context ?? new ExecutionContext());

        return ExecuteAndEnrichEvidenceAsync(
            request,
            plan,
            providerPlan,
            executionContext,
            executionIr,
            cancellationToken,
            authorizationEvidence: authorization.Evidence);
    }

    private string BuildProviderPlanCacheKey(
        SemanticPlan plan,
        SecurityExecutionContext? security)
    {
        var shape = SemanticPlanFingerprint.CreateShapeKey(plan);

        // Security-bearing requests are partitioned by the exact warrant digest.
        // The compiled provider plan may be semantically identical across callers,
        // but an authority-bearing cache entry must never become an authority
        // confused cache artifact. This is deliberately conservative: warrant
        // changes create a new cache partition rather than relying on inferred
        // equivalence of grants/constraints.
        return security is null
            ? _cacheNamespace + ":" + shape
            : _cacheNamespace + ":authority:" + security.AuthorityCachePartition + ":" + shape;
    }

    private void ValidateWarrant(SemanticRequest request, SemanticOperation operation, bool consumeReplay)
    {
        var security = request.Security;
        if (security is null)
            return;

        if (_warrantKeyResolver is null)
            throw new InvalidOperationException(
                "A security warrant was supplied, but no warrant key resolver is configured.");

        SecurityWarrantVerifier.Verify(
            security.Warrant,
            _warrantKeyResolver,
            DateTimeOffset.UtcNow,
            _expectedWarrantIssuer,
            security.Audience);

        var capabilities = operation.Root.TraverseDepthFirst()
            .Select(node => _securityContract.Capabilities.FirstOrDefault(c =>
                c.TargetEntityId == node.EntityId &&
                string.Equals(c.Operation, "read", StringComparison.Ordinal)))
            .Where(c => c is not null)
            .Cast<SemanticCapability>()
            .DistinctBy(c => c.Id, StringComparer.Ordinal)
            .ToArray();

        if (capabilities.Length == 0)
            throw new InvalidOperationException(
                $"No security capability contract exists for root entity '{operation.Root.EntityId}' and its semantic composition.");

        var requestedFields = operation.Root.TraverseDepthFirst()
            .SelectMany(node => node.Fields
                .Select(fieldId => _model.TryGet(node.EntityId, out var entity)
                    ? entity.Fields.FirstOrDefault(f => f.Id == fieldId)?.Name
                    : null))
            .Where(name => name is not null)
            .Cast<string>()
            .ToArray();

        var composition = SecurityCapabilityComposition.Validate(
            capabilities,
            security.Warrant,
            security.Subject,
            security.Audience,
            security.Tenant,
            security.ResourceScope,
            requestedFields,
            request.Options?.Limit);

        if (!composition.IsSatisfied)
            throw new UnauthorizedAccessException(composition.FailureReason);

        if (consumeReplay)
        {
            if (_warrantReplayStore is null)
                throw new InvalidOperationException(
                    "Executing a warrant-backed request requires a warrant replay store.");
            SecurityWarrantReplayGuard.Consume(
                security.Warrant,
                _warrantReplayStore,
                DateTimeOffset.UtcNow);
        }
    }

    private SemanticPlan BuildSecuredPlan(SemanticAuthorizationResult authorization)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        authorization.EnsureMatches(_contract);

        var planned = _planner.Plan(_contract, authorization);
        var optimized = _planOptimizer.Optimize(planned);
        if (!optimized.SecurityProof.IsSatisfied)
            throw new InvalidOperationException(
                "The optimized semantic plan does not carry a satisfied security-preservation proof.");

        var capability = _securityContract.Capabilities.FirstOrDefault(c =>
            c.TargetEntityId == authorization.Operation.Root.EntityId &&
            string.Equals(c.Operation, "read", StringComparison.Ordinal));

        if (capability is null)
            throw new InvalidOperationException(
                $"No security capability contract exists for root entity '{authorization.Operation.Root.EntityId}' and operation 'read'.");

        var plan = SecurityInvariantPlanRequirements.Attach(
            optimized.Plan,
            capability.EffectiveSecurityInvariants);
        if (plan.EffectiveSecurityInvariants.Count == 0)
            throw new InvalidOperationException(
                "The semantic execution contract is empty; no executable plan may be produced.");

        foreach (var id in plan.EffectiveSecurityInvariants)
            if (!SecurityInvariantRegistry.Contains(id))
                throw new InvalidOperationException(
                    $"The semantic plan contains unknown security invariant '{id}'.");

        return plan;
    }

    private async Task<ExecutionResult> ExecuteAndEnrichEvidenceAsync(
        SemanticRequest request,
        SemanticPlan plan,
        ProviderPlan providerPlan,
        ExecutionContext context,
        ExecutionIR executionIr,
        CancellationToken cancellationToken,
        PlanApproval? approval = null,
        SemanticAuthorizationEvidence? authorizationEvidence = null)
    {
        SecurityInvariantExecutionGate.EnsureExecutable(providerPlan, executionIr);

        if (authorizationEvidence is null)
            throw new InvalidOperationException(
                "Executable semantic plans require authorization evidence bound to the same semantic contract.");

        var binding = plan.AuthorizationBinding
                      ?? throw new InvalidOperationException(
                          "Executable semantic plans require an authorization binding.");
        binding.EnsureMatches(_contract, authorizationEvidence);

        // Final authorization check immediately before provider execution. This is
        // intentionally after cache lookup and provider-plan construction so a
        // previously valid artifact cannot bypass the current authority state.
        var currentAuthority = _executionAuthorizationAuthorityResolver is null
            ? null
            : await _executionAuthorizationAuthorityResolver(authorizationEvidence, cancellationToken);
        await _executionAuthorizationRevalidator.ValidateAsync(
            _contract, authorizationEvidence, currentAuthority, cancellationToken);

        context.EnsureWithinDeadline();
        var startedAt = DateTimeOffset.UtcNow;
        using var deadlineCts = context.CreateDeadlineCancellationSource(cancellationToken);
        var result = await _provider.ExecuteAsync(providerPlan, context, deadlineCts.Token);
        context.EnsureWithinDeadline();
        var completedAt = DateTimeOffset.UtcNow;
        if (result.Evidence is null)
            return result;

        var intentFingerprint = ExecutionEvidenceFactory.Hash(
            $"intent-v{_versions.IntentVersion}|{JsonSerializer.Serialize(request)}");
        var authorizationFingerprint = binding.AuthorizationFingerprint;
        var evidence = result.Evidence with
        {
            PlanFingerprint = SemanticPlanFingerprint.Create(plan),
            IntentFingerprint = intentFingerprint,
            AuthorizationFingerprint = authorizationFingerprint
        };

        var effects = plan.Root.Operation == ExecutionOperation.Scan
            ? new[] { "read" }
            : new[] { "read", "relationship-traversal" };
        var affectedNodeIds = EnumerateNodes(plan.Root).Select(node => node.Id);
        var receipt = ExecutionReceiptFactory.Create(
            requestId: Guid.NewGuid().ToString("N"),
            evidence,
            ExecutionReceiptFactory.FingerprintResult(result),
            affectedNodeIds,
            effects,
            startedAt,
            completedAt,
            _versions.CapabilityContractVersion,
            _versions.CapabilityVersion,
            _versions.IntentVersion,
            _versions.PlanVersion,
            _versions.SemanticModelVersion,
            approval?.ApprovalId,
            approval?.ApprovedBy,
            approval?.ApprovedAt);

        return result with
        {
            Evidence = evidence,
            Receipt = receipt
        };
    }

    private static IEnumerable<SemanticPlanNode> EnumerateNodes(SemanticPlanNode node)
    {
        yield return node;
        foreach (var child in node.Children)
        foreach (var descendant in EnumerateNodes(child))
            yield return descendant;
    }

    private static ExecutionContext AttachPaginationContext(SemanticPlan plan, ExecutionContext context)
    {
        var options = plan.Root.QueryOptions;
        if (options?.Limit is null && options?.Offset is null)
            return context;

        var values = new Dictionary<string, object?>(context.EffectiveValues, StringComparer.Ordinal);
        if (options.Limit is { } limit)
            values[ExecutionContextKeys.PaginationLimit] = limit;
        if (options.Offset is { } offset)
            values[ExecutionContextKeys.PaginationOffset] = offset;
        values[ExecutionContextKeys.PaginationHasCursor] = options.After is not null;

        return new ExecutionContext(values);
    }
}