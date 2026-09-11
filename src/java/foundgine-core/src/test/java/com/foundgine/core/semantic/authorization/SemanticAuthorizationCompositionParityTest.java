package com.foundgine.core.semantic.authorization;

import com.foundgine.core.abstractions.AuthorizationDecision;
import org.junit.jupiter.api.Test;

import static org.junit.jupiter.api.Assertions.*;

/** Mirrors the C# authorization-composition invariants. */
class SemanticAuthorizationCompositionParityTest {
    @Test
    void compositionIsMonotonicAndCannotBroadenAccess() {
        assertEquals(AuthorizationDecision.ALLOWED,
                SemanticAuthorizationCapabilityComposition.compose(AuthorizationDecision.ALLOWED));
        assertEquals(AuthorizationDecision.DENIED,
                SemanticAuthorizationCapabilityComposition.compose(
                        AuthorizationDecision.ALLOWED, AuthorizationDecision.DENIED));
        assertEquals(AuthorizationDecision.DENIED,
                SemanticAuthorizationCapabilityComposition.compose(
                        AuthorizationDecision.DENIED, AuthorizationDecision.ALLOWED));
    }

    @Test
    void emptyCompositionStartsAllowed() {
        assertEquals(AuthorizationDecision.ALLOWED,
                SemanticAuthorizationCapabilityComposition.compose(java.util.List.of()));
    }
}
