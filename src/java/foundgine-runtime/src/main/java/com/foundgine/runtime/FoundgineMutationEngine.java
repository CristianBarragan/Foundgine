package com.foundgine.runtime;

import com.fasterxml.jackson.databind.ObjectMapper;
import com.foundgine.core.abstractions.*;
import com.foundgine.core.execution.CancellationToken;
import com.foundgine.core.execution.ExecutionContext;
import com.foundgine.core.execution.mutation.*;
import com.foundgine.core.semantic.*;
import com.foundgine.core.semantic.authorization.*;
import com.foundgine.core.semantic.capabilities.*;
import com.foundgine.core.semantic.mutation.*;
import com.foundgine.core.semantic.planning.mutation.*;
import com.foundgine.core.semantic.query.*;
import com.foundgine.core.semantic.security.*;
import com.foundgine.core.semantic.security.execution.*;
import com.foundgine.core.semantic.security.warrants.*;

import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.time.Instant;
import java.util.*;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.CompletionStage;

/**
 * Port of {@code Foundgine.Runtime.FoundgineMutationEngine}.
 *
 * <p>
 * Mutations use the same fail-closed sequence as the C# implementation:
 * resource validation -> warrant verification -> semantic planning ->
 * authorization -> invariant derivation -> semantic-to-execution lowering ->
 * concrete provider certification -> replay consumption -> provider execution.
 */
public final class FoundgineMutationEngine implements IFoundgineMutations {
	private static final ObjectMapper MAPPER = new ObjectMapper();

	private final MutationSchema schema;
	private final SemanticModel model;
	private final ISemanticAuthorizationPolicy policy;
	private final IMutationBatchExecutionProvider provider;
	private final SemanticCapabilityContract securityContract;
	private final ISecurityWarrantKeyResolver warrantKeyResolver;
	private final String expectedWarrantIssuer;
	private final ISecurityWarrantReplayStore warrantReplayStore;
	private final SecurityResourceLimits securityResourceLimits;

	public FoundgineMutationEngine(MutationSchema schema, ISemanticAuthorizationPolicy policy,
			IMutationBatchExecutionProvider provider) {
		this(schema, policy, provider, null, null, null, null, null);
	}

	public FoundgineMutationEngine(MutationSchema schema, ISemanticAuthorizationPolicy policy,
			IMutationBatchExecutionProvider provider, SemanticModel model,
			ISecurityWarrantKeyResolver warrantKeyResolver, String expectedWarrantIssuer,
			ISecurityWarrantReplayStore warrantReplayStore, SecurityResourceLimits securityResourceLimits) {
		this.schema = Objects.requireNonNull(schema, "schema");
		this.model = model;
		this.policy = Objects.requireNonNull(policy, "policy");
		this.provider = Objects.requireNonNull(provider, "provider");
		this.securityContract = model == null ? new SemanticCapabilityContract(1, List.of())
				: SemanticCapabilityContractDiscovery.describe(model, policy);
		SecurityInvariantContractValidator.ensureContractValid(securityContract);
		this.warrantKeyResolver = warrantKeyResolver;
		this.expectedWarrantIssuer = expectedWarrantIssuer;
		this.warrantReplayStore = warrantReplayStore;
		this.securityResourceLimits = securityResourceLimits != null ? securityResourceLimits
				: new SecurityResourceLimits();
		this.securityResourceLimits.validate();
	}

	@Override
	public MutationDryRunResult dryRun(SemanticMutationRequest request) {
		SemanticMutationPlan plan = authorizeAndPlan(Objects.requireNonNull(request, "request"));
		return describe(plan);
	}

	@Override
	public CompletionStage<MutationExecutionResult> executeAsync(SemanticMutationRequest request,
			ExecutionContext context, CancellationToken cancellationToken) {
		Objects.requireNonNull(request, "request");
		if (cancellationToken != null)
			cancellationToken.throwIfCancellationRequested();
		SemanticMutationPlan plan = authorizeAndPlan(request);
		return executePlan(plan, request.security(), null, context, cancellationToken);
	}

	@Override
	public MutationPlanApproval approve(SemanticMutationRequest request, String approvedBy) {
		Objects.requireNonNull(request, "request");
		if (approvedBy == null || approvedBy.isBlank())
			throw new IllegalArgumentException("approvedBy must not be null or blank");
		MutationDryRunResult dryRun = dryRun(request);
		return new MutationPlanApproval(request, UUID.randomUUID().toString().replace("-", ""),
				dryRun.planFingerprint(), approvedBy, Instant.now());
	}

	@Override
	public CompletionStage<MutationExecutionResult> executeApprovedAsync(MutationPlanApproval approval,
			ExecutionContext context, CancellationToken cancellationToken) {
		Objects.requireNonNull(approval, "approval");
		if (cancellationToken != null)
			cancellationToken.throwIfCancellationRequested();

		SemanticMutationPlan plan = authorizeAndPlan(approval.request());
		String fingerprint = fingerprint(plan);
		if (!fingerprint.equals(approval.planFingerprint()))
			throw new IllegalStateException(
					"The approved mutation plan changed after approval. Re-run dry-run and obtain a new approval.");

		return executePlan(plan, approval.request().security(), approval, context, cancellationToken);
	}

	private CompletionStage<MutationExecutionResult> executePlan(SemanticMutationPlan plan,
			com.foundgine.core.semantic.security.execution.SecurityExecutionContext security,
			MutationPlanApproval approval, ExecutionContext context, CancellationToken cancellationToken) {
		ExecutionMutationIR ir = new SemanticMutationExecutionLowerer(schema).lower(plan);
		ExecutionContext executionContext = context != null ? context : ExecutionContext.EMPTY;
		executionContext.ensureWithinDeadline();

		try (var deadlineSource = executionContext.createDeadlineCancellationSource(
				cancellationToken != null ? cancellationToken : CancellationToken.NONE)) {
			MutationExecutionSecurityCertificate certificate = MutationExecutionSecurityGate.certify(ir, provider,
					provider.getClass().getName(), plan.requiredSecurityInvariants().stream()
							.filter(FoundgineMutationEngine::isEnginePreservedInvariant).toList());
			MutationExecutionSecurityGate.ensureExecutable(ir, provider, certificate);

			if (security != null)
				consumeWarrantReplay(security);

			MutationBatchResult result = provider.executeBatch(ir, executionContext, deadlineSource.token());
			executionContext.ensureWithinDeadline();
			return CompletableFuture.completedFuture(new MutationExecutionResult(result, fingerprint(plan),
					fingerprintResult(result), approval != null ? approval.approvalId() : null,
					approval != null ? approval.approvedBy() : null));
		}
	}

	private void validateWarrant(SemanticMutationRequest request) {
		var security = request.security();
		if (security == null)
			return;
		if (warrantKeyResolver == null)
			throw new IllegalStateException(
					"A security warrant was supplied, but no warrant key resolver is configured.");

		SecurityWarrantVerifier.verify(security.warrant(), warrantKeyResolver, Instant.now(), expectedWarrantIssuer,
				security.audience());

		for (var operation : request.graph().operations()) {
			var capability = securityContract.capabilities().stream()
					.filter(c -> c.targetEntityId().equals(operation.entity())
							&& c.operation().equalsIgnoreCase(operation.kind().name().toLowerCase(Locale.ROOT)))
					.findFirst()
					.orElseThrow(() -> new SecurityException("No security capability contract exists for entity '"
							+ operation.entity() + "' and operation '" + operation.kind() + "'."));

			if (!SecurityWarrantAuthorization.allows(security.warrant(), security.subject(), security.audience(),
					capability.id(), capability.operation(), security.tenant(), security.resourceScope()))
				throw new SecurityException("Security warrant does not authorize capability '" + capability.id()
						+ "' for subject '" + security.subject() + ".");

			validateWarrantAllowedFields(operation, security);
		}
	}

	private void validateWarrantAllowedFields(SemanticMutationOperation operation,
			com.foundgine.core.semantic.security.execution.SecurityExecutionContext security) {
		List<String> allowed = security.warrant().constraints().allowedFields();
		if (allowed.isEmpty())
			return;
		if (model == null)
			throw new IllegalStateException(
					"A mutation warrant specifies allowed fields, but no SemanticModel is configured to resolve them.");

		SemanticEntity entity = model.get(operation.entity());
		LinkedHashSet<String> requested = new LinkedHashSet<>();
		for (var field : operation.fields()) {
			if (field.source() != null)
				continue;
			entity.fields().stream().filter(x -> x.id().equals(field.field())).findFirst()
					.ifPresent(x -> requested.add(x.name()));
		}
		for (var field : operation.returnFields())
			entity.fields().stream().filter(x -> x.id().equals(field)).findFirst()
					.ifPresent(x -> requested.add(x.name()));
		requested.addAll(filterFields(operation.filter(), entity));

		if (requested.stream().anyMatch(x -> !allowed.contains(x)))
			throw new SecurityException("Security warrant does not authorize one or more requested mutation fields on '"
					+ entity.name() + "'.");
	}

	private static List<String> filterFields(SemanticFilterExpression filter, SemanticEntity entity) {
		if (filter == null)
			return List.of();
		List<String> result = new ArrayList<>();
		if (filter instanceof SemanticFieldFilter f) {
			entity.fields().stream().filter(x -> x.id().equals(f.field())).findFirst()
					.ifPresent(x -> result.add(x.name()));
		} else if (filter instanceof SemanticAndFilter a) {
			for (var e : a.expressions())
				result.addAll(filterFields(e, entity));
		} else if (filter instanceof SemanticOrFilter o) {
			for (var e : o.expressions())
				result.addAll(filterFields(e, entity));
		}
		return result;
	}

	private void consumeWarrantReplay(
			com.foundgine.core.semantic.security.execution.SecurityExecutionContext security) {
		if (warrantReplayStore == null)
			throw new IllegalStateException("Executing a warrant-backed mutation requires a warrant replay store.");
		SecurityWarrantReplayGuard.consume(security.warrant(), warrantReplayStore, Instant.now());
	}

	private SemanticMutationPlan authorizeAndPlan(SemanticMutationRequest request) {
		MutationSecurityResourceLimitValidator.validate(request, securityResourceLimits);
		validateWarrant(request);

		SemanticMutationPlan semanticPlan = new SemanticMutationPlanner().plan(request.graph());
		SemanticMutationPlan authorizedPlan = new MutationAuthorizer(schema, policy).authorize(semanticPlan);
		List<String> required = requiredSecurityInvariantsFor(authorizedPlan);
		return new SemanticMutationPlan(authorizedPlan.operations(), authorizedPlan.dependencies(), required);
	}

	private List<String> requiredSecurityInvariantsFor(SemanticMutationPlan plan) {
		LinkedHashSet<String> required = new LinkedHashSet<>();
		for (var operation : plan.operations()) {
			var capability = securityContract.capabilities().stream()
					.filter(c -> c.targetEntityId().equals(operation.entity())
							&& c.operation().equalsIgnoreCase(operation.kind().name().toLowerCase(Locale.ROOT)))
					.findFirst()
					.orElseThrow(() -> new IllegalStateException("No security capability contract exists for entity '"
							+ operation.entity() + "' and operation '" + operation.kind() + "'."));
			required.addAll(capability.effectiveSecurityInvariants());
		}
		if (required.isEmpty())
			throw new IllegalStateException(
					"A mutation execution cannot proceed without explicit security invariants.");
		return required.stream().sorted().toList();
	}

	private static boolean isEnginePreservedInvariant(String id) {
		return switch (id) {
		case SecurityInvariantIds.AUTHORIZATION_REQUIRED, SecurityInvariantIds.RUNTIME_AUTHORIZATION,
				SecurityInvariantIds.FIELD_VISIBILITY, SecurityInvariantIds.RELATIONSHIP_VISIBILITY ->
			true;
		default -> false;
		};
	}

	private MutationDryRunResult describe(SemanticMutationPlan plan) {
		List<MutationPlanOperation> operations = new ArrayList<>();
		for (int i = 0; i < plan.operations().size(); i++) {
			var x = plan.operations().get(i);
			var entity = schema.getEntity(x.entity());
			operations.add(new MutationPlanOperation(i, entity.name(), x.kind().name(),
					x.fields().stream().map(f -> Long.toString(f.field().value())).toList(),
					x.returnFields().stream().map(f -> Long.toString(f.value())).toList()));
		}
		List<String> effects = plan.operations().stream().flatMap(x -> x.effects().stream()).map(this::formatEffect)
				.toList();
		return new MutationDryRunResult(fingerprint(plan), operations, effects);
	}

	private String formatEffect(SemanticMutationEffect effect) {
		return effect.kind() + ":" + schema.getEntity(effect.entity()).name()
				+ (effect.field() != null ? "." + effect.field().value() : "");
	}

	private static String fingerprint(SemanticMutationPlan plan) {
		try {
			String canonical = MAPPER
					.writeValueAsString(Map.of("version", "mutation-plan-v1", "operations", plan.operations(),
							"dependencies", plan.dependencies(), "security", plan.requiredSecurityInvariants()));
			return sha256Hex(canonical);
		} catch (Exception e) {
			throw new IllegalStateException("Unable to fingerprint mutation plan.", e);
		}
	}

	private static String fingerprintResult(MutationBatchResult result) {
		try {
			List<Map<String, Object>> rows = new ArrayList<>();
			for (var x : result.results()) {
				Map<String, Object> row = new LinkedHashMap<>();
				row.put("affectedRows", x.affectedRows());
				if (x.returnedValues() != null) {
					row.put("returned",
							x.returnedValues().entrySet().stream()
									.sorted(Map.Entry.comparingByKey(Comparator.comparingLong(FieldId::value)))
									.map(e -> Map.of("field", e.getKey().value(), "value", e.getValue())).toList());
				}
				rows.add(row);
			}
			return sha256Hex(MAPPER.writeValueAsString(rows));
		} catch (Exception e) {
			throw new IllegalStateException("Unable to fingerprint mutation result.", e);
		}
	}

	private static String sha256Hex(String text) {
		try {
			byte[] digest = MessageDigest.getInstance("SHA-256").digest(text.getBytes(StandardCharsets.UTF_8));
			StringBuilder out = new StringBuilder(digest.length * 2);
			for (byte b : digest)
				out.append(String.format("%02X", b));
			return out.toString();
		} catch (Exception e) {
			throw new IllegalStateException(e);
		}
	}
}
