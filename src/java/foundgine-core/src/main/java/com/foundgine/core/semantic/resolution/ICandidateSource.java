package com.foundgine.core.semantic.resolution;

import com.foundgine.core.abstractions.*;
import java.util.List;

public interface ICandidateSource {
	List<IdentityCandidate> findByIdentity(EntityId entityType, String identityValue);

	List<IdentityCandidate> findByRelationship(RelationshipId relationshipId, String sourceIdentityValue);
}
