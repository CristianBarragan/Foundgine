package com.foundgine.core.execution;

import com.foundgine.core.semantic.SemanticContractSnapshot;
import com.foundgine.core.semantic.authorization.SemanticAuthorizationEvidence;

/**
 *
 * <p>Revalidates authorization at the final execution boundary. Implementations
 * may consult a database, distributed authority, cache, or another trusted
 * control plane.
 *
 * <p>Provided as a two-overload pair: the full method plus a default
 * method that forwards {@link CancellationToken#NONE}, for callers that
 * don't need cancellation.
 */
public interface IExecutionAuthorizationRevalidator {
    void validate(SemanticContractSnapshot contract, SemanticAuthorizationEvidence evidence,
                   ExecutionAuthorizationAuthorityState currentAuthority, CancellationToken cancellationToken);

    default void validate(SemanticContractSnapshot contract, SemanticAuthorizationEvidence evidence,
                           ExecutionAuthorizationAuthorityState currentAuthority) {
        validate(contract, evidence, currentAuthority, CancellationToken.NONE);
    }
}