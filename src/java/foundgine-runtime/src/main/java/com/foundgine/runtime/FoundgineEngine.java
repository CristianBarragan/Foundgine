package com.foundgine.runtime;

import com.fasterxml.jackson.databind.ObjectMapper;
import com.foundgine.core.execution.CancellationToken;
import com.foundgine.core.execution.ExecutionAuthorizationAuthorityState;
import com.foundgine.core.execution.ExecutionContext;
import com.foundgine.core.execution.ExecutionContextKeys;
import com.foundgine.core.execution.ExecutionEvidence;
import com.foundgine.core.execution.ExecutionEvidenceFactory;
import com.foundgine.core.execution.ExecutionIR;
import com.foundgine.core.execution.ExecutionIRCompiler;
import com.foundgine.core.execution.ExecutionReceipt;
import com.foundgine.core.execution.ExecutionReceiptFactory;
import com.foundgine.core.execution.ExecutionResult;
import com.foundgine.core.execution.IExecutionAuthorizationRevalidator;
import com.foundgine.core.execution.IExecutionProvider;
import com.foundgine.core.execution.IProviderPlanCache;
import com.foundgine.core.execution.MemoryProviderPlanCache;
import com.foundgine.core.execution.ProviderPlan;
import com.foundgine.core.execution.ProviderPlanCacheExtensions;
import com.foundgine.core.execution.SecurityInvariantExecutionGate;
import com.foundgine.core.execution.SecurityInvariantProofGate;
import com.foundgine.core.execution.SemanticExecutionAuthorizationRevalidator;
import com.foundgine.core.execution.IProviderPlanCompiler;
import com.foundgine.core.semantic.SemanticContractSnapshot;
import com.foundgine.core.semantic.SemanticModel;
import com.foundgine.core.semantic.SemanticRequest;
import com.foundgine.core.semantic.SemanticVersionSet;
import com.foundgine.core.semantic.authorization.ISemanticAuthorizationPolicy;
import com.foundgine.core.semantic.authorization.SemanticAuthorizationCapabilities;
import com.foundgine.core.semantic.authorization.SemanticAuthorizationCapabilityDiscovery;
import com.foundgine.core.semantic.authorization.SemanticAuthorizationEvidence;
import com.foundgine.core.semantic.authorization.SemanticAuthorizationResult;
import com.foundgine.core.semantic.authorization.SemanticAuthorizer;
import com.foundgine.core.semantic.capabilities.SemanticCapability;
import com.foundgine.core.semantic.capabilities.SemanticCapabilityContract;
import com.foundgine.core.semantic.capabilities.SemanticCapabilityContractDiscovery;
import com.foundgine.core.semantic.intent.ReadIntent;
import com.foundgine.core.semantic.intent.ReadIntentCompiler;
import com.foundgine.core.semantic.ir.SemanticOperation;
import com.foundgine.core.semantic.ir.SemanticOperationCompiler;
import com.foundgine.core.semantic.planning.IPlanOptimizer;
import com.foundgine.core.semantic.planning.IPlanner;
import com.foundgine.core.semantic.planning.PlanInspector;
import com.foundgine.core.semantic.planning.Planner;
import com.foundgine.core.semantic.planning.SemanticPlan;
import com.foundgine.core.semantic.planning.SemanticPlanFingerprint;
import com.foundgine.core.semantic.planning.SemanticPlanNode;
import com.foundgine.core.semantic.planning.SemanticPlanOptimizationResult;
import com.foundgine.core.semantic.planning.SemanticPlanOptimizer;
import com.foundgine.core.semantic.planning.SecurityInvariantPlanRequirements;
import com.foundgine.core.semantic.resolution.SemanticRequestResolver;
import com.foundgine.core.semantic.security.SecurityInvariantContractValidator;
import com.foundgine.core.semantic.security.SecurityInvariantRegistry;
import com.foundgine.core.semantic.security.SecurityCapabilityComposition;
import com.foundgine.core.semantic.security.SecurityCapabilityCompositionResult;
import com.foundgine.core.semantic.security.execution.SecurityExecutionContext;
import com.foundgine.core.semantic.security.execution.SecurityResourceLimitValidator;
import com.foundgine.core.semantic.security.execution.SecurityResourceLimits;
import com.foundgine.core.semantic.security.warrants.ISecurityWarrantKeyResolver;
import com.foundgine.core.semantic.security.warrants.ISecurityWarrantReplayStore;
import com.foundgine.core.semantic.security.warrants.SecurityWarrantReplayGuard;
import com.foundgine.core.semantic.security.warrants.SecurityWarrantVerifier;

import java.time.Instant;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
import java.util.Objects;
import java.util.UUID;
import java.util.concurrent.CompletionStage;
import java.util.function.BiFunction;

/**
 * Port of {@code Foundgine.Runtime.FoundgineEngine}.
 *
 * <p>Application-facing facade over the Foundgine semantic execution
 * pipeline. Applications normally obtain this through {@link
 * FoundgineOptions} / {@code FoundgineBuilder} rather than constructing it
 * directly.
 *
 * <p><b>Porting decisions:</b>
 * <ul>
 *   <li>The C# {@code Func<SemanticAuthorizationEvidence, CancellationToken,
 *       ValueTask<ExecutionAuthorizationAuthorityState?>>} authority resolver
 *       is ported as a synchronous {@link BiFunction} returning a nullable
 *       {@link ExecutionAuthorizationAuthorityState}, matching this port's
 *       existing decision (see {@link IExecutionAuthorizationRevalidator}) to
 *       make execution-time authority revalidation a synchronous, fail-closed
 *       check rather than an awaited call. A caller whose authority source is
 *       genuinely remote/async should resolve it before calling {@code
 *       executeAsync} and supply it via a resolver that reads a
 *       pre-fetched value.</li>
 *   <li>The C# {@code async}/{@code await} pipeline is ported as ordinary
 *       synchronous Java methods (resolution, compilation, authorization,
 *       planning and caching are all CPU-bound here) that only return a
 *       {@link CompletionStage} at the single true asynchronous boundary:
 *       the call to {@link IExecutionProvider#executeAsync}. Evidence/receipt
 *       enrichment, which in C# happens after the {@code await}, is ported as
 *       a {@code thenApply} continuation on that stage.</li>
 *   <li>C#'s {@code JsonSerializer.Serialize(request)} (System.Text.Json) is
 *       ported using the Jackson {@link ObjectMapper} already used elsewhere
 *       in this port (see {@code JsonReadIntentAdapter}); the exact JSON text
 *       differs from the C# serializer; it is only ever hashed for a
 *       same-process intent fingerprint, and hashing does not need to be
 *       stable across languages.</li>
 * </ul>
 */
public final class FoundgineEngine implements IFoundgine {

    private static final ObjectMapper MAPPER = new ObjectMapper();

    private final SemanticModel model;
    private final SemanticContractSnapshot contract;
    private final ISemanticAuthorizationPolicy authorizationPolicy;
    private final IPlanner planner;
    private final IPlanOptimizer planOptimizer;
    private final IProviderPlanCompiler compiler;
    private final IExecutionProvider provider;
    private final IProviderPlanCache planCache;
    private final SemanticVersionSet versions;
    private final SemanticCapabilityContract securityContract;
    private final ISecurityWarrantKeyResolver warrantKeyResolver;
    private final String expectedWarrantIssuer;
    private final ISecurityWarrantReplayStore warrantReplayStore;
    private final SecurityResourceLimits securityResourceLimits;
    private final IExecutionAuthorizationRevalidator executionAuthorizationRevalidator;
    private final BiFunction<SemanticAuthorizationEvidence, CancellationToken, ExecutionAuthorizationAuthorityState>
            executionAuthorizationAuthorityResolver;
    private final String cacheNamespace = UUID.randomUUID().toString().replace("-", "");

    FoundgineEngine(
            FoundgineOptions options,
            SemanticContractSnapshot contract,
            IProviderPlanCompiler compiler,
            IExecutionProvider provider) {
        Objects.requireNonNull(options, "options");
        this.model = options.model();
        if (this.model == null)
            throw new IllegalStateException("FoundgineOptions.model() must be configured.");
        this.contract = Objects.requireNonNull(contract, "contract");
        this.authorizationPolicy = options.authorizationPolicy();
        if (this.authorizationPolicy == null)
            throw new IllegalStateException("FoundgineOptions.authorizationPolicy() must be configured.");
        this.planner = new Planner();
        this.planOptimizer = new SemanticPlanOptimizer();
        this.compiler = Objects.requireNonNull(compiler, "compiler");
        this.provider = Objects.requireNonNull(provider, "provider");
        this.planCache = options.planCache() != null ? options.planCache() : new MemoryProviderPlanCache();
        this.versions = SemanticVersionSet.of(this.model);
        this.securityContract = SemanticCapabilityContractDiscovery.describe(this.model, this.authorizationPolicy);
        SecurityInvariantContractValidator.ensureContractValid(this.securityContract);
        this.warrantKeyResolver = options.warrantKeyResolver();
        this.expectedWarrantIssuer = options.expectedWarrantIssuer();
        this.warrantReplayStore = options.warrantReplayStore();
        this.securityResourceLimits =
                options.securityResourceLimits() != null ? options.securityResourceLimits() : new SecurityResourceLimits();
        this.executionAuthorizationRevalidator = options.executionAuthorizationRevalidator() != null
                ? options.executionAuthorizationRevalidator()
                : new SemanticExecutionAuthorizationRevalidator();
        this.executionAuthorizationAuthorityResolver = options.executionAuthorizationAuthorityResolver();
        this.securityResourceLimits.validate();
    }

    /**
     * Convenience overload for callers (largely tests) that build {@link
     * FoundgineOptions} directly and do not already hold a frozen {@link
     * SemanticContractSnapshot}. The snapshot is derived from {@link
     * FoundgineOptions#model()} the same way the Hosting builder does at
     * startup.
     */
    FoundgineEngine(FoundgineOptions options, IProviderPlanCompiler compiler, IExecutionProvider provider) {
        this(options, createContract(options), compiler, provider);
    }

    private static SemanticContractSnapshot createContract(FoundgineOptions options) {
        Objects.requireNonNull(options, "options");
        SemanticModel model = options.model();
        if (model == null)
            throw new IllegalStateException("FoundgineOptions.model() must be configured.");
        return model.freeze().createSnapshot();
    }

    /**
     * Internal-compatible constructor for adapters/tests that intentionally
     * provide the orchestration components themselves.
     */
    FoundgineEngine(
            SemanticModel model,
            ISemanticAuthorizationPolicy authorizationPolicy,
            IPlanner planner,
            IProviderPlanCompiler compiler,
            IExecutionProvider provider,
            IProviderPlanCache planCache) {
        this.model = Objects.requireNonNull(model, "model");
        this.contract = this.model.freeze().createSnapshot();
        this.authorizationPolicy = Objects.requireNonNull(authorizationPolicy, "authorizationPolicy");
        this.planner = Objects.requireNonNull(planner, "planner");
        this.planOptimizer = new SemanticPlanOptimizer();
        this.compiler = Objects.requireNonNull(compiler, "compiler");
        this.provider = Objects.requireNonNull(provider, "provider");
        this.planCache = planCache != null ? planCache : new MemoryProviderPlanCache();
        this.versions = SemanticVersionSet.of(this.model);
        this.securityContract = SemanticCapabilityContractDiscovery.describe(this.model, this.authorizationPolicy);
        SecurityInvariantContractValidator.ensureContractValid(this.securityContract);
        this.warrantKeyResolver = null;
        this.expectedWarrantIssuer = null;
        this.warrantReplayStore = null;
        this.securityResourceLimits = new SecurityResourceLimits();
        this.executionAuthorizationRevalidator = new SemanticExecutionAuthorizationRevalidator();
        this.executionAuthorizationAuthorityResolver = null;
    }

    FoundgineEngine(
            SemanticModel model,
            ISemanticAuthorizationPolicy authorizationPolicy,
            IPlanner planner,
            IProviderPlanCompiler compiler,
            IExecutionProvider provider) {
        this(model, authorizationPolicy, planner, compiler, provider, null);
    }

    @Override
    public SemanticAuthorizationCapabilities describeCapabilities() {
        return SemanticAuthorizationCapabilityDiscovery.describe(model, authorizationPolicy);
    }

    @Override
    public SemanticCapabilityContract describeCapabilityContract() {
        return SemanticCapabilityContractDiscovery.describe(model, authorizationPolicy);
    }

    @Override
    public SemanticCapabilityContract describeCapabilityContract(SecurityExecutionContext security) {
        Objects.requireNonNull(security, "security");
        validateDiscoveryWarrant(security);

        SemanticCapabilityContract contract = SemanticCapabilityContractDiscovery.describe(model, authorizationPolicy);
        List<SemanticCapability> visible = contract.capabilities().stream()
                .filter(capability -> com.foundgine.core.semantic.security.warrants.SecurityWarrantAuthorization.allows(
                        security.warrant(),
                        security.subject(),
                        security.audience(),
                        capability.id(),
                        capability.operation(),
                        security.tenant(),
                        security.resourceScope()))
                .toList();

        return new SemanticCapabilityContract(contract.version(), visible);
    }

    private void validateDiscoveryWarrant(SecurityExecutionContext security) {
        if (warrantKeyResolver == null)
            throw new IllegalStateException("Warrant-backed capability discovery requires a warrant key resolver.");

        SecurityWarrantVerifier.verify(
                security.warrant(), warrantKeyResolver, Instant.now(), expectedWarrantIssuer, security.audience());
    }

    @Override
    public SemanticVersionSet describeVersionSet() {
        return versions;
    }

    @Override
    public DryRunResult dryRun(SemanticRequest request) {
        Objects.requireNonNull(request, "request");
        SecurityResourceLimitValidator.validate(request, securityResourceLimits);

        var graph = new SemanticRequestResolver(contract).resolve(request);
        SemanticOperation semanticOperation = SemanticOperationCompiler.compile(graph);
        validateWarrant(request, semanticOperation, false);
        SemanticAuthorizationResult authorization =
                new SemanticAuthorizer(authorizationPolicy).authorizeWithEvidence(contract, semanticOperation);
        authorization.ensureMatches(contract);
        SemanticPlan plan = buildSecuredPlan(authorization);
        return new DryRunResult(PlanInspector.inspect(plan));
    }

    @Override
    public PlanApproval approvePlan(SemanticRequest request, String approvedBy) {
        Objects.requireNonNull(request, "request");
        if (approvedBy == null || approvedBy.isBlank())
            throw new IllegalArgumentException("approvedBy must not be null or blank");

        DryRunResult dryRun = dryRun(request);
        return new PlanApproval(
                request,
                UUID.randomUUID().toString().replace("-", ""),
                dryRun.inspection().planFingerprint(),
                versions.semanticModelVersion(),
                versions.capabilityContractVersion(),
                versions.capabilityVersion(),
                versions.intentVersion(),
                versions.planVersion(),
                approvedBy,
                Instant.now());
    }

    @Override
    public CompletionStage<ExecutionResult> executeApprovedAsync(
            PlanApproval approval, ExecutionContext context, CancellationToken cancellationToken) {
        Objects.requireNonNull(approval, "approval");
        SecurityResourceLimitValidator.validate(approval.request(), securityResourceLimits);

        if (!versions.semanticModelVersion().equals(approval.semanticModelVersion())
                || approval.capabilityContractVersion() != versions.capabilityContractVersion()
                || approval.capabilityVersion() != versions.capabilityVersion()
                || approval.intentVersion() != versions.intentVersion()
                || approval.planVersion() != versions.planVersion()) {
            throw new IllegalStateException(
                    "The approval was created against an incompatible semantic version set. Re-run dry-run and obtain a new approval.");
        }

        var graph = new SemanticRequestResolver(contract).resolve(approval.request());
        SemanticOperation semanticOperation = SemanticOperationCompiler.compile(graph);
        validateWarrant(approval.request(), semanticOperation, true);
        SemanticAuthorizationResult authorization =
                new SemanticAuthorizer(authorizationPolicy).authorizeWithEvidence(contract, semanticOperation);
        authorization.ensureMatches(contract);
        SemanticPlan plan = buildSecuredPlan(authorization);
        String currentFingerprint = SemanticPlanFingerprint.create(plan);

        if (!currentFingerprint.equals(approval.planFingerprint())) {
            throw new IllegalStateException(
                    "The approved plan no longer matches the current authorized plan. Re-run dry-run and obtain a new approval.");
        }

        ExecutionIR executionIr = ExecutionIRCompiler.compile(plan);
        String cacheKey = buildProviderPlanCacheKey(plan, approval.request().security());
        ProviderPlan providerPlan = ProviderPlanCacheExtensions.getOrAdd(
                planCache,
                cacheKey,
                () -> SecurityInvariantProofGate.attachAndValidate(compiler.compile(executionIr), executionIr, compiler));

        ExecutionContext executionContext =
                attachPaginationContext(plan, context != null ? context : ExecutionContext.EMPTY);
        return executeAndEnrichEvidenceAsync(
                approval.request(), plan, providerPlan, executionContext, executionIr, cancellationToken,
                approval, authorization.evidence());
    }

    @Override
    public CompletionStage<ExecutionResult> executeAsync(
            ReadIntent intent, ExecutionContext context, CancellationToken cancellationToken) {
        Objects.requireNonNull(intent, "intent");
        SemanticRequest request = new ReadIntentCompiler(model).compile(intent);
        return executeAsync(request, context, cancellationToken);
    }

    @Override
    public CompletionStage<ExecutionResult> executeAsync(
            SemanticRequest request, ExecutionContext context, CancellationToken cancellationToken) {
        Objects.requireNonNull(request, "request");
        SecurityResourceLimitValidator.validate(request, securityResourceLimits);

        var graph = new SemanticRequestResolver(contract).resolve(request);
        SemanticOperation semanticOperation = SemanticOperationCompiler.compile(graph);
        validateWarrant(request, semanticOperation, true);
        SemanticAuthorizationResult authorization =
                new SemanticAuthorizer(authorizationPolicy).authorizeWithEvidence(contract, semanticOperation);
        authorization.ensureMatches(contract);
        SemanticPlan plan = buildSecuredPlan(authorization);
        ExecutionIR executionIr = ExecutionIRCompiler.compile(plan);
        String cacheKey = buildProviderPlanCacheKey(plan, request.security());
        ProviderPlan providerPlan = ProviderPlanCacheExtensions.getOrAdd(
                planCache,
                cacheKey,
                () -> SecurityInvariantProofGate.attachAndValidate(compiler.compile(executionIr), executionIr, compiler));

        ExecutionContext executionContext =
                attachPaginationContext(plan, context != null ? context : ExecutionContext.EMPTY);

        return executeAndEnrichEvidenceAsync(
                request, plan, providerPlan, executionContext, executionIr, cancellationToken, null,
                authorization.evidence());
    }

    private String buildProviderPlanCacheKey(SemanticPlan plan, SecurityExecutionContext security) {
        String shape = SemanticPlanFingerprint.createShapeKey(plan);

        // Security-bearing requests are partitioned by the exact warrant digest.
        // The compiled provider plan may be semantically identical across callers,
        // but an authority-bearing cache entry must never become an authority
        // confused cache artifact. This is deliberately conservative: warrant
        // changes create a new cache partition rather than relying on inferred
        // equivalence of grants/constraints.
        return security == null
                ? cacheNamespace + ":" + shape
                : cacheNamespace + ":authority:" + security.authorityCachePartition() + ":" + shape;
    }

    private void validateWarrant(SemanticRequest request, SemanticOperation operation, boolean consumeReplay) {
        SecurityExecutionContext security = request.security();
        if (security == null)
            return;

        if (warrantKeyResolver == null)
            throw new IllegalStateException("A security warrant was supplied, but no warrant key resolver is configured.");

        SecurityWarrantVerifier.verify(
                security.warrant(), warrantKeyResolver, Instant.now(), expectedWarrantIssuer, security.audience());

        List<SemanticCapability> capabilities = new ArrayList<>();
        for (var node : traverseDepthFirst(operation.root())) {
            securityContract.capabilities().stream()
                    .filter(c -> c.targetEntityId().equals(node.entityId()) && "read".equals(c.operation()))
                    .findFirst()
                    .ifPresent(capabilities::add);
        }

        if (capabilities.isEmpty())
            throw new IllegalStateException("No security capability contract exists for root entity '"
                    + operation.root().entityId() + "' and its semantic composition.");

        List<String> requestedFields = new ArrayList<>();
        for (var node : traverseDepthFirst(operation.root())) {
            for (var fieldId : node.fields()) {
                model.tryGet(node.entityId(), entity -> entity.fields().stream()
                        .filter(f -> f.id().equals(fieldId))
                        .findFirst()
                        .ifPresent(f -> requestedFields.add(f.name())));
            }
        }

        SecurityCapabilityCompositionResult composition = SecurityCapabilityComposition.validate(
                capabilities,
                security.warrant(),
                security.subject(),
                security.audience(),
                security.tenant(),
                security.resourceScope(),
                requestedFields,
                request.options() != null && request.options().limit() != null
                        ? Long.valueOf(request.options().limit())
                        : null,
                null);

        if (!composition.isSatisfied())
            throw new SecurityException(composition.failureReason());

        if (consumeReplay) {
            if (warrantReplayStore == null)
                throw new IllegalStateException("Executing a warrant-backed request requires a warrant replay store.");
            SecurityWarrantReplayGuard.consume(security.warrant(), warrantReplayStore, Instant.now());
        }
    }

    private static List<com.foundgine.core.semantic.ir.SemanticReadNode> traverseDepthFirst(
            com.foundgine.core.semantic.ir.SemanticReadNode node) {
        List<com.foundgine.core.semantic.ir.SemanticReadNode> result = new ArrayList<>();
        result.add(node);
        for (var child : node.children())
            result.addAll(traverseDepthFirst(child));
        return result;
    }

    private SemanticPlan buildSecuredPlan(SemanticAuthorizationResult authorization) {
        Objects.requireNonNull(authorization, "authorization");
        authorization.ensureMatches(contract);

        SemanticPlan planned = planner.plan(contract, authorization);
        SemanticPlanOptimizationResult optimized = planOptimizer.optimize(planned);
        if (!optimized.securityProof().isSatisfied())
            throw new IllegalStateException(
                    "The optimized semantic plan does not carry a satisfied security-preservation proof.");

        SemanticCapability capability = securityContract.capabilities().stream()
                .filter(c -> c.targetEntityId().equals(authorization.operation().root().entityId())
                        && "read".equals(c.operation()))
                .findFirst()
                .orElseThrow(() -> new IllegalStateException("No security capability contract exists for root entity '"
                        + authorization.operation().root().entityId() + "' and operation 'read'."));

        SemanticPlan plan =
                SecurityInvariantPlanRequirements.attach(optimized.plan(), capability.effectiveSecurityInvariants());
        if (plan.effectiveSecurityInvariants().isEmpty())
            throw new IllegalStateException("The semantic execution contract is empty; no executable plan may be produced.");

        for (String id : plan.effectiveSecurityInvariants())
            if (!SecurityInvariantRegistry.contains(id))
                throw new IllegalStateException("The semantic plan contains unknown security invariant '" + id + "'.");

        return plan;
    }

    private CompletionStage<ExecutionResult> executeAndEnrichEvidenceAsync(
            SemanticRequest request,
            SemanticPlan plan,
            ProviderPlan providerPlan,
            ExecutionContext context,
            ExecutionIR executionIr,
            CancellationToken cancellationToken,
            PlanApproval approval,
            SemanticAuthorizationEvidence authorizationEvidence) {
        SecurityInvariantExecutionGate.ensureExecutable(providerPlan, executionIr);

        if (authorizationEvidence == null)
            throw new IllegalStateException(
                    "Executable semantic plans require authorization evidence bound to the same semantic contract.");

        var binding = plan.authorizationBinding();
        if (binding == null)
            throw new IllegalStateException("Executable semantic plans require an authorization binding.");
        binding.ensureMatches(contract, authorizationEvidence);

        // Final authorization check immediately before provider execution. This is
        // intentionally after cache lookup and provider-plan construction so a
        // previously valid artifact cannot bypass the current authority state.
        ExecutionAuthorizationAuthorityState currentAuthority = executionAuthorizationAuthorityResolver == null
                ? null
                : executionAuthorizationAuthorityResolver.apply(authorizationEvidence, cancellationToken);
        executionAuthorizationRevalidator.validate(contract, authorizationEvidence, currentAuthority, cancellationToken);

        context.ensureWithinDeadline();
        Instant startedAt = Instant.now();
        var deadlineCts = context.createDeadlineCancellationSource(cancellationToken);
        return provider.executeAsync(providerPlan, context, deadlineCts.token()).thenApply(result -> {
            context.ensureWithinDeadline();
            Instant completedAt = Instant.now();
            if (result.evidence() == null)
                return result;

            String intentFingerprint;
            try {
                intentFingerprint = ExecutionEvidenceFactory.hash(
                        "intent-v" + versions.intentVersion() + "|" + MAPPER.writeValueAsString(request));
            } catch (Exception e) {
                throw new IllegalStateException("Unable to serialize semantic request for intent fingerprinting.", e);
            }
            String authorizationFingerprint = binding.authorizationFingerprint();
            ExecutionEvidence baseEvidence = result.evidence();
            ExecutionEvidence evidence = new ExecutionEvidence(
                    baseEvidence.provider(),
                    SemanticPlanFingerprint.create(plan),
                    baseEvidence.authorizedNodeIds(),
                    baseEvidence.rowsReturned(),
                    baseEvidence.elapsedMilliseconds(),
                    baseEvidence.providerOperationFingerprint(),
                    intentFingerprint,
                    authorizationFingerprint,
                    baseEvidence.warrantId(),
                    baseEvidence.warrantDigest(),
                    baseEvidence.securityInvariantDigest(),
                    baseEvidence.authorizationVersion());

            List<String> effects = plan.root().operation() == com.foundgine.core.semantic.planning.ExecutionOperation.SCAN
                    ? List.of("read")
                    : List.of("read", "relationship-traversal");
            List<Integer> affectedNodeIds = enumerateNodes(plan.root()).stream().map(SemanticPlanNode::id).toList();
            ExecutionReceipt receipt = ExecutionReceiptFactory.create(
                    UUID.randomUUID().toString().replace("-", ""),
                    evidence,
                    ExecutionReceiptFactory.fingerprintResult(result),
                    affectedNodeIds,
                    effects,
                    startedAt,
                    completedAt,
                    versions.capabilityContractVersion(),
                    versions.capabilityVersion(),
                    versions.intentVersion(),
                    versions.planVersion(),
                    versions.semanticModelVersion(),
                    approval != null ? approval.approvalId() : null,
                    approval != null ? approval.approvedBy() : null,
                    approval != null ? approval.approvedAt() : null);

            return new ExecutionResult(result.rows(), result.pageInfo(), evidence, receipt);
        });
    }

    private static List<SemanticPlanNode> enumerateNodes(SemanticPlanNode node) {
        List<SemanticPlanNode> result = new ArrayList<>();
        result.add(node);
        for (var child : node.children())
            result.addAll(enumerateNodes(child));
        return result;
    }

    private static ExecutionContext attachPaginationContext(SemanticPlan plan, ExecutionContext context) {
        var options = plan.root().queryOptions();
        if (options == null || (options.limit() == null && options.offset() == null))
            return context;

        Map<String, Object> values = new HashMap<>(context.effectiveValues());
        if (options.limit() != null)
            values.put(ExecutionContextKeys.PAGINATION_LIMIT, options.limit());
        if (options.offset() != null)
            values.put(ExecutionContextKeys.PAGINATION_OFFSET, options.offset());
        values.put(ExecutionContextKeys.PAGINATION_HAS_CURSOR, options.after() != null);

        return new ExecutionContext(values);
    }
}
