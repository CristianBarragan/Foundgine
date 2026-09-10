package com.foundgine.core.abstractions;

/**
 * Port of {@code Foundgine.Core.Abstractions.IMutationSchema}.
 *
 * <p>Narrow, provider-neutral schema contract required by mutation
 * planning. It deliberately exposes stable identities and key mappings
 * only; concrete metadata types remain outside the planning layer.
 */
public interface MutationSchema {
    MutationEntitySchema getEntity(EntityId entityId);

    MutationRelationshipSchema getRelationship(RelationshipId relationshipId);
}
