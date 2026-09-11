package com.foundgine.core.execution;

import com.foundgine.core.semantic.SemanticContractSnapshot;
import com.foundgine.core.semantic.authorization.SemanticAuthorizationEvidence;
import java.util.Objects;

/** Default fail-closed revalidator for contract-bound authorization evidence. */
public final class SemanticExecutionAuthorizationRevalidator implements IExecutionAuthorizationRevalidator {
    @Override
    public void validate(SemanticContractSnapshot contract, SemanticAuthorizationEvidence evidence, ExecutionAuthorizationAuthorityState currentAuthority, CancellationToken cancellationToken) {
        Objects.requireNonNull(contract,"contract"); Objects.requireNonNull(evidence,"evidence"); Objects.requireNonNull(cancellationToken,"cancellationToken");
        cancellationToken.throwIfCancellationRequested(); evidence.ensureMatches(contract);
        if(currentAuthority==null)return;
        if(!currentAuthority.allowed())throw new SecurityException("The current authorization authority is revoked; execution fails closed.");
        if(currentAuthority.fingerprint()==null||currentAuthority.fingerprint().isBlank())throw new IllegalStateException("The current authorization authority fingerprint is missing; execution fails closed.");
        if(evidence.authorizationVersion()==null)throw new IllegalStateException("Authorization evidence has no authority version and cannot be revalidated against current authority.");
        if(evidence.authorizationVersion()!=currentAuthority.version())throw new IllegalStateException("Authorization evidence version "+evidence.authorizationVersion()+" is no longer current; current authority version is "+currentAuthority.version()+". Execution fails closed.");
        if(evidence.authorizationAuthorityFingerprint()==null||!evidence.authorizationAuthorityFingerprint().equals(currentAuthority.fingerprint()))throw new IllegalStateException("Authorization evidence does not match the current authorization authority. Execution fails closed.");
    }
}
