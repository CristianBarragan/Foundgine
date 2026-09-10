package com.foundgine.core.execution;

import com.foundgine.core.semantic.SemanticContractSnapshot;
import com.foundgine.core.semantic.authorization.SemanticAuthorizationEvidence;

/**
 * Port of {@code Foundgine.Core.Execution.IExecutionAuthorizationRevalidator}.
 *
 * <p>Revalidates authorization at the final execution boundary. Implementations
 * may consult a database, distributed authority, cache, or another trusted
 * control plane.
 *
 * <p>C#'s {@code CancellationToken cancellationToken = default} optional
 * parameter is ported as a two-overload pair: the full method plus a default
 * method that forwards {@link CancellationToken#NONE}, matching this port's
 * established convention for optional-parameter C# methods.
 */
public interface IExecutionAuthorizationRevalidator {
    void validate(SemanticContractSnapshot contract, SemanticAuthorizationEvidence evidence,
                   ExecutionAuthorizationAuthorityState currentAuthority, CancellationToken cancellationToken);

    default void validate(SemanticContractSnapshot contract, SemanticAuthorizationEvidence evidence,
                           ExecutionAuthorizationAuthorityState currentAuthority) {
        validate(contract, evidence, currentAuthority, CancellationToken.NONE);
    }
}
