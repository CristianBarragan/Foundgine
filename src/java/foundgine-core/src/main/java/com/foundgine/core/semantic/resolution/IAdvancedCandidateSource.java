package com.foundgine.core.semantic.resolution;

import com.foundgine.core.abstractions.*;
import java.time.OffsetDateTime;
import java.util.List;
import java.util.Map;

public interface IAdvancedCandidateSource extends ICandidateSource {
	List<IdentityCandidate> findByCompositeIdentity(EntityId entityType, Map<String, String> identityValues);

	List<IdentityCandidate> findByTemporalIdentity(EntityId entityType, String identityValue, OffsetDateTime asOf);
}
