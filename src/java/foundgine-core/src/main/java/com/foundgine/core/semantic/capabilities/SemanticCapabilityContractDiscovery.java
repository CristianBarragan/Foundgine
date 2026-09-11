package com.foundgine.core.semantic.capabilities;

import com.foundgine.core.abstractions.AuthorizationDecision;
import com.foundgine.core.abstractions.AuthorizationOperation;
import com.foundgine.core.abstractions.AuthorizationPredicate;
import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.semantic.SemanticEntity;
import com.foundgine.core.semantic.SemanticModel;
import com.foundgine.core.semantic.SemanticRelationship;
import com.foundgine.core.semantic.SemanticTraversal;
import com.foundgine.core.semantic.SemanticVersionSet;
import com.foundgine.core.semantic.authorization.ISemanticAuthorizationPolicy;
import com.foundgine.core.semantic.authorization.SemanticAuthorizationCapability;
import com.foundgine.core.semantic.authorization.SemanticAuthorizationCapabilityDiscovery;
import com.foundgine.core.semantic.authorization.SemanticFieldAuthorizationCapability;

import java.util.ArrayList;
import java.util.Comparator;
import java.util.List;
import java.util.Objects;

/**
 * Port of {@code Foundgine.Core.Semantic.Capabilities.SemanticCapabilityContractDiscovery}.
 *
 * <p>Builds the canonical, machine-readable capability contract for a
 * frozen semantic model under an authorization policy. Capability
 * discovery ({@link SemanticAuthorizationCapabilityDiscovery}) intentionally
 * hides authorization predicates; this class rehydrates only the top-level
 * entity read/write decision with the exact policy predicate so
 * planning/security consumers can carry it forward, while the descriptive
 * authorization capability surface underneath stays predicate-free.
 *
 * <p><b>Porting decisions:</b>
 * <ul>
 *   <li>C# {@code yield return} iterator methods ({@code BuildCapabilities},
 *       {@code BuildMutationActions}) are ported as methods returning a
 *       fully materialized {@link List}, built in the same emission order.</li>
 *   <li>The C# {@code entity with { Read = ..., Write = ... }} non-destructive
 *       mutation is ported as constructing a new
 *       {@link SemanticAuthorizationCapability} record with the same fields
 *       except the patched {@code read}/{@code write}.</li>
 *   <li>{@code SemanticCapability} object-initializer syntax (setting
 *       {@code Operation}/{@code HasSideEffects}/{@code IsIdempotent} on top
 *       of the 9 positional constructor args) is ported via the private
 *       {@link #capability} helper, which fills in the same C# {@code init}
 *       defaults for {@code version} and {@code requiredSecurityInvariants}
 *       that every call site here relies on.</li>
 *   <li>The C# extension method {@code ClrTypeName} is ported as the private
 *       static method {@link #clrTypeName}; {@code Type.FullName ?? Name} is
 *       ported as {@link Class#getTypeName()}, which the JVM never returns
 *       null for.</li>
 * </ul>
 */
public final class SemanticCapabilityContractDiscovery {
    public static final int CURRENT_VERSION = 1;

    private SemanticCapabilityContractDiscovery() {
    }

    public static SemanticCapabilityContract describe(SemanticModel model, ISemanticAuthorizationPolicy policy) {
        Objects.requireNonNull(model, "model");
        Objects.requireNonNull(policy, "policy");

        var discovered = SemanticAuthorizationCapabilityDiscovery.describe(model, policy);

        var capabilities = discovered.entities().stream()
                .map(entity -> new SemanticAuthorizationCapability(
                        entity.entityId(),
                        entity.name(),
                        preservePredicate(entity.read(), policy.getPredicate(entity.entityId(), AuthorizationOperation.READ)),
                        preservePredicate(entity.write(), policy.getPredicate(entity.entityId(), AuthorizationOperation.WRITE)),
                        entity.fields(),
                        entity.relationships()))
                .flatMap(entity -> buildCapabilities(model, entity, policy).stream())
                .sorted(Comparator.comparing(SemanticCapability::id))
                .toList();

        return new SemanticCapabilityContract(CURRENT_VERSION, capabilities);
    }

    private static AuthorizationDecision preservePredicate(AuthorizationDecision decision, AuthorizationPredicate predicate) {
        return predicate != null ? AuthorizationDecision.conditional(predicate) : decision;
    }

    private static List<SemanticCapability> buildCapabilities(
            SemanticModel model,
            SemanticAuthorizationCapability entity,
            ISemanticAuthorizationPolicy policy) {
        List<SemanticCapability> result = new ArrayList<>();

        var readFields = entity.fields().stream()
                .filter(x -> x.read().isAllowed())
                .map(SemanticFieldAuthorizationCapability::name)
                .sorted(String.CASE_INSENSITIVE_ORDER)
                .toList();
        var readRelationships = java.util.stream.Stream.concat(
                        entity.relationships().stream()
                                .filter(x -> x.read().isAllowed())
                                .map(x -> x.name()),
                        model.traversals().stream()
                                .filter(x -> x.source().equals(entity.entityId()) && traversalIsReadable(model, x, policy))
                                .map(SemanticTraversal::name))
                .distinct()
                .sorted(String.CASE_INSENSITIVE_ORDER)
                .toList();

        result.add(capability(
                entity.name() + ".read",
                "Read " + entity.name(),
                entity.entityId(),
                entity.read(),
                List.of(),
                List.of(),
                List.of(),
                readFields,
                readRelationships,
                "read",
                false,
                true));

        var writeFields = entity.fields().stream()
                .filter(x -> x.write().isAllowed())
                .map(SemanticFieldAuthorizationCapability::name)
                .sorted(String.CASE_INSENSITIVE_ORDER)
                .toList();
        var writeRelationships = entity.relationships().stream()
                .filter(x -> x.write().isAllowed())
                .map(x -> x.name())
                .sorted(String.CASE_INSENSITIVE_ORDER)
                .toList();

        result.add(capability(
                entity.name() + ".write",
                "Write " + entity.name(),
                entity.entityId(),
                entity.write(),
                buildWriteInputs(model, entity),
                buildWriteConstraints(),
                entity.write().isAllowed()
                        ? List.of(new SemanticCapabilityEffect(
                                "data.write",
                                "May modify " + entity.name() + " data when execution-time authorization permits it."))
                        : List.of(),
                writeFields,
                writeRelationships,
                "write",
                entity.write().isAllowed(),
                false));

        if (entity.write().isAllowed()) {
            result.addAll(buildMutationActions(model, entity));
        }

        for (var relationship : entity.relationships()) {
            if (!relationship.read().isAllowed()) {
                continue;
            }
            result.add(capability(
                    entity.name() + "." + relationship.name() + ".traverse",
                    "Traverse " + entity.name() + "." + relationship.name(),
                    relationship.targetEntityId(),
                    relationship.read(),
                    List.of(),
                    List.of(),
                    List.of(),
                    List.of(),
                    List.of(),
                    "traverse",
                    false,
                    true));
        }

        for (var traversal : model.traversals()) {
            if (!traversal.source().equals(entity.entityId()) || !traversalIsReadable(model, traversal, policy)) {
                continue;
            }
            var pathDescription = traversal.path().stream()
                    .map(x -> Long.toUnsignedString(x.value()))
                    .reduce((a, b) -> a + " -> " + b)
                    .orElse("");
            result.add(capability(
                    entity.name() + "." + traversal.name() + ".traverse",
                    "Traverse " + entity.name() + "." + traversal.name(),
                    traversal.target(),
                    AuthorizationDecision.ALLOWED,
                    List.of(),
                    List.of(new SemanticCapabilityConstraint(
                            "semantic-path",
                            "Logical traversal expands through relationship path " + pathDescription
                                    + "; every hop remains subject to execution-time authorization.")),
                    List.of(),
                    List.of(),
                    List.of(),
                    "traverse",
                    false,
                    true));
        }

        return result;
    }

    private static boolean traversalIsReadable(
            SemanticModel model,
            SemanticTraversal traversal,
            ISemanticAuthorizationPolicy policy) {
        var current = model.get(traversal.source());
        for (var relationshipId : traversal.path()) {
            SemanticRelationship relationship = current.relationships().stream()
                    .filter(x -> x.id().equals(relationshipId))
                    .findFirst()
                    .orElse(null);
            if (relationship == null
                    || !policy.getRelationshipAccess(current.id(), relationship.id(), AuthorizationOperation.READ).isAllowed()) {
                return false;
            }

            current = model.get(relationship.target());
            if (!policy.getEntityAccess(current.id(), AuthorizationOperation.READ).isAllowed()) {
                return false;
            }
        }

        return true;
    }

    private static List<SemanticCapabilityConstraint> buildWriteConstraints() {
        return List.of(
                new SemanticCapabilityConstraint("authorization", "Execution-time authorization must permit the requested mutation."),
                new SemanticCapabilityConstraint("writable-fields", "Every requested field must be writable under the effective authorization policy."));
    }

    private static List<SemanticCapability> buildMutationActions(SemanticModel model, SemanticAuthorizationCapability entity) {
        List<SemanticCapability> result = new ArrayList<>();

        for (var action : new String[] {"create", "update", "delete", "upsert"}) {
            List<SemanticCapabilityConstraint> constraints = switch (action) {
                case "create" -> List.of(
                        new SemanticCapabilityConstraint("writable-fields", "Every supplied field must be writable."));
                case "update" -> List.of(
                        new SemanticCapabilityConstraint("target-selection", "A target filter or equivalent identity selection is required."),
                        new SemanticCapabilityConstraint("writable-fields", "Every supplied field must be writable."));
                case "delete" -> List.of(
                        new SemanticCapabilityConstraint("target-selection", "A target filter or equivalent identity selection is required."));
                case "upsert" -> List.of(
                        new SemanticCapabilityConstraint("conflict-key", "A conflict key or equivalent identity must determine the upsert target."),
                        new SemanticCapabilityConstraint("writable-fields", "Every supplied field must be writable."));
                default -> List.of();
            };

            List<SemanticCapabilityEffect> effects = new ArrayList<>();
            effects.add(new SemanticCapabilityEffect(
                    "data." + action,
                    "May " + action + " " + entity.name() + " data when execution-time authorization permits it."));
            if (action.equals("create") || action.equals("update") || action.equals("upsert")) {
                effects.add(new SemanticCapabilityEffect("field.mutation", "May change writable field values."));
            }

            var displayName = switch (action) {
                case "create" -> "Create";
                case "update" -> "Update";
                case "delete" -> "Delete";
                case "upsert" -> "Upsert";
                default -> action;
            };

            var writeFields = entity.fields().stream()
                    .filter(x -> x.write().isAllowed())
                    .map(x -> x.name())
                    .sorted(String.CASE_INSENSITIVE_ORDER)
                    .toList();
            var writeRelationships = entity.relationships().stream()
                    .filter(x -> x.write().isAllowed())
                    .map(x -> x.name())
                    .sorted(String.CASE_INSENSITIVE_ORDER)
                    .toList();

            result.add(capability(
                    entity.name() + "." + action,
                    displayName + " " + entity.name(),
                    entity.entityId(),
                    entity.write(),
                    action.equals("delete") ? List.of() : buildWriteInputs(model, entity),
                    constraints,
                    effects,
                    writeFields,
                    writeRelationships,
                    action,
                    entity.write().isAllowed(),
                    action.equals("update") || action.equals("delete") || action.equals("upsert")));
        }

        return result;
    }

    private static List<SemanticCapabilityInput> buildWriteInputs(SemanticModel model, SemanticAuthorizationCapability entity) {
        return entity.fields().stream()
                .filter(x -> x.write().isAllowed())
                .map(field -> new SemanticCapabilityInput(
                        field.name(),
                        clrTypeName(field, model, entity.entityId()),
                        false,
                        "Writable field on " + entity.name() + "."))
                .sorted(Comparator.comparing(SemanticCapabilityInput::name, String.CASE_INSENSITIVE_ORDER))
                .toList();
    }

    private static String clrTypeName(SemanticFieldAuthorizationCapability field, SemanticModel model, EntityId entityId) {
        var semanticField = model.get(entityId).fields().stream()
                .filter(x -> x.id().equals(field.fieldId()))
                .findFirst()
                .orElseThrow();
        return semanticField.clrType().getTypeName();
    }

    private static SemanticCapability capability(
            String id,
            String name,
            EntityId targetEntityId,
            AuthorizationDecision access,
            List<SemanticCapabilityInput> inputs,
            List<SemanticCapabilityConstraint> constraints,
            List<SemanticCapabilityEffect> effects,
            List<String> fields,
            List<String> relationships,
            String operation,
            boolean hasSideEffects,
            boolean isIdempotent) {
        return new SemanticCapability(
                id, name, targetEntityId, access, inputs, constraints, effects, fields, relationships,
                operation, hasSideEffects, isIdempotent, SemanticVersionSet.CURRENT_CAPABILITY_VERSION, List.of());
    }
}
