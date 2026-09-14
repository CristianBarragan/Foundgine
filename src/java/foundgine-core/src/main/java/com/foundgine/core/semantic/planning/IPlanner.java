package com.foundgine.core.semantic.planning;

import com.foundgine.core.semantic.SemanticGraph;
import com.foundgine.core.semantic.SemanticContractSnapshot;
import com.foundgine.core.semantic.authorization.SemanticAuthorizationResult;
import com.foundgine.core.semantic.ir.SemanticOperation;

public interface IPlanner {
	SemanticPlan plan(SemanticOperation operation);

	default SemanticPlan plan(SemanticGraph graph) {
		return plan(com.foundgine.core.semantic.ir.SemanticOperationCompiler.compile(graph));
	}

	default SemanticPlan plan(SemanticContractSnapshot contract, SemanticOperation operation) {
		if (contract == null || operation == null)
			throw new NullPointerException();
		SemanticOperationContractValidator.validate(operation, contract);
		return plan(operation);
	}

	default SemanticPlan plan(SemanticContractSnapshot contract, SemanticAuthorizationResult authorization) {
		if (contract == null || authorization == null)
			throw new NullPointerException();
		authorization.ensureMatches(contract);
		var p = plan(contract, authorization.operation());
		return new SemanticPlan(p.root(), p.requiredSecurityInvariants(),
				SemanticPlanAuthorizationBinding.create(contract, authorization.evidence()));
	}
}
