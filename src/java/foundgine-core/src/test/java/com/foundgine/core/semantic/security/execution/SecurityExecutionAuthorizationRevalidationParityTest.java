package com.foundgine.core.semantic.security.execution;

import com.foundgine.core.execution.CancellationToken;
import com.foundgine.core.execution.ExecutionAuthorizationAuthorityState;
import com.foundgine.core.execution.SemanticExecutionAuthorizationRevalidator;
import com.foundgine.core.semantic.SemanticContractSnapshot;
import com.foundgine.core.semantic.authorization.SemanticAuthorizationEvidence;
import org.junit.jupiter.api.Test;

import static org.junit.jupiter.api.Assertions.*;

class SecurityExecutionAuthorizationRevalidationParityTest {
	private static SemanticContractSnapshot contract() {
		return new com.foundgine.core.semantic.SemanticModelBuilder()
				.entity(com.foundgine.core.abstractions.EntityId.create("Customer"), "Customer",
						e -> e.identity(com.foundgine.core.abstractions.FieldId.create("Customer", "Id"), "Id"))
				.build().freeze().createSnapshot();
	}

	@Test
	void revokedAuthorityFailsClosed() {
		var revalidator = new SemanticExecutionAuthorizationRevalidator();
		var contract = contract();
		var evidence = new SemanticAuthorizationEvidence(contract.contractFingerprint(), "a", 4L, "authority");
		var authority = new ExecutionAuthorizationAuthorityState(4L, "authority", false);

		assertThrows(SecurityException.class,
				() -> revalidator.validate(contract, evidence, authority, new CancellationToken()));
	}

	@Test
	void authorityVersionChangeFailsClosed() {
		var revalidator = new SemanticExecutionAuthorizationRevalidator();
		var contract = contract();
		var evidence = new SemanticAuthorizationEvidence(contract.contractFingerprint(), "a", 4L, "authority");
		var authority = new ExecutionAuthorizationAuthorityState(5L, "authority", true);

		assertThrows(IllegalStateException.class,
				() -> revalidator.validate(contract, evidence, authority, new CancellationToken()));
	}
}
