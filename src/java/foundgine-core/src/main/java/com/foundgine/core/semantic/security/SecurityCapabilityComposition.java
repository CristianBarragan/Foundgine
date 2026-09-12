package com.foundgine.core.semantic.security;

import com.foundgine.core.semantic.capabilities.SemanticCapability;
import com.foundgine.core.semantic.security.warrants.SecurityWarrant;
import com.foundgine.core.semantic.security.warrants.SecurityWarrantAuthorization;

import java.math.BigDecimal;
import java.util.ArrayList;
import java.util.Comparator;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Objects;

/**
 *
 * <p>Validates composition of multiple capabilities as a single security contract.
 * Composition never unions authority: every component must be independently
 * authorized and the resulting authority is bounded by the intersection of
 * the active warrant constraints.
 *
 * <p>The three trailing parameters ({@code requestedFields},
 * {@code requestedResults}, {@code requestedAmount}, all defaulting to
 * {@code null}) are also available as a 6-arg overload (matching call sites
 * that rely on the defaults) plus the full 9-arg method.
 */
public final class SecurityCapabilityComposition {

    private SecurityCapabilityComposition() {
    }

    public static SecurityCapabilityCompositionResult validate(
            Iterable<SemanticCapability> capabilities,
            SecurityWarrant warrant,
            String subject,
            String audience,
            String tenant,
            String resourceScope) {
        return validate(capabilities, warrant, subject, audience, tenant, resourceScope, null, null, null);
    }

    public static SecurityCapabilityCompositionResult validate(
            Iterable<SemanticCapability> capabilities,
            SecurityWarrant warrant,
            String subject,
            String audience,
            String tenant,
            String resourceScope,
            Iterable<String> requestedFields,
            Long requestedResults,
            BigDecimal requestedAmount) {
        Objects.requireNonNull(capabilities);
        Objects.requireNonNull(warrant);

        Map<String, SemanticCapability> distinct = new LinkedHashMap<>();
        for (SemanticCapability capability : capabilities)
            distinct.putIfAbsent(capability.id(), capability);

        List<SemanticCapability> components = distinct.values().stream()
                .sorted(Comparator.comparing(SemanticCapability::id))
                .toList();

        if (components.isEmpty())
            return SecurityCapabilityCompositionResult.rejected(
                    "A security composition must contain at least one capability.");

        for (SemanticCapability capability : components) {
            SecurityInvariantContractValidator.ensureValid(capability);

            // The composed resourceScope describes the request as a whole, not
            // any single component: components may legitimately have different,
            // narrower grant scopes (e.g. "customer/*" vs "order/*"). Requiring
            // each component's own grant to match the composed-level scope would
            // reject valid compositions across independently-scoped capabilities.
            // The composed scope is still enforced once, holistically, against
            // the warrant's constraints below.
            if (!SecurityWarrantAuthorization.allows(
                    warrant,
                    subject,
                    audience,
                    capability.id(),
                    capability.operation(),
                    tenant,
                    resourceScope,
                    requestedResults,
                    requestedAmount,
                    false)) {
                return SecurityCapabilityCompositionResult.rejected(
                        "Capability composition is not authorized because '" + capability.id()
                                + "' is not independently authorized.");
            }
        }

        List<String> fields = new ArrayList<>();
        if (requestedFields != null) {
            for (String field : requestedFields) {
                if (field != null && !field.isBlank() && !fields.contains(field))
                    fields.add(field);
            }
        }

        List<String> allowedFields = warrant.constraints().allowedFields();
        if (!allowedFields.isEmpty()) {
            for (String field : fields) {
                if (!allowedFields.contains(field)) {
                    return SecurityCapabilityCompositionResult.rejected(
                            "Capability composition requests a field outside the warrant's allowed field set.");
                }
            }
        }

        // A composed operation may only use one caller/tenant/resource authority.
        // There is deliberately no union operation here: incompatible components
        // fail closed rather than producing a broader synthetic authority.
        List<String> allowedTenants = warrant.constraints().allowedTenants();
        if (tenant != null && !allowedTenants.isEmpty() && !allowedTenants.contains(tenant)) {
            return SecurityCapabilityCompositionResult.rejected(
                    "Capability composition crosses the warrant tenant boundary.");
        }

        List<String> resourceScopes = warrant.constraints().resourceScopes();
        if (resourceScope != null && !resourceScopes.isEmpty() && !resourceScopes.contains(resourceScope)) {
            return SecurityCapabilityCompositionResult.rejected(
                    "Capability composition crosses the warrant resource boundary.");
        }

        List<String> invariants = new ArrayList<>();
        for (SemanticCapability capability : components) {
            for (String invariant : capability.effectiveSecurityInvariants()) {
                if (!invariants.contains(invariant))
                    invariants.add(invariant);
            }
        }
        invariants = invariants.stream().sorted().toList();

        for (String invariant : invariants) {
            if (!SecurityInvariantRegistry.contains(invariant)) {
                return SecurityCapabilityCompositionResult.rejected(
                        "Capability composition contains unknown security invariant '" + invariant + "'.");
            }
        }

        return SecurityCapabilityCompositionResult.accepted(components, invariants);
    }
}