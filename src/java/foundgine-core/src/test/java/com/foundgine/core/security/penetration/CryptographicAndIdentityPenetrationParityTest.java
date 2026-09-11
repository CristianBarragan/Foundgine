package com.foundgine.core.security.penetration;

import com.foundgine.core.semantic.security.warrants.*;
import org.junit.jupiter.api.Test;
import java.security.*;
import java.security.interfaces.RSAPublicKey;
import java.time.Instant;
import java.util.List;
import static org.junit.jupiter.api.Assertions.*;

/** Port of CryptographicAndIdentityPenetrationTests. */
class CryptographicAndIdentityPenetrationParityTest {
    private static SecurityWarrant create(Instant now, String issuer, String subject, String audience,
                                           SecurityWarrantConstraints constraints) {
        return SecurityWarrant.ofDefaults("warrant-1", issuer, subject, audience,
                List.of(new CapabilityGrant("Account.read", "read", List.of("account-1"))),
                constraints == null ? SecurityWarrantConstraints.UNRESTRICTED : constraints,
                now.minusSeconds(60), now.plusSeconds(600), "nonce-1", "key-1", null, new byte[0]);
    }

    private static SecurityWarrant sign(SecurityWarrant w, KeyPair kp) { return SecurityWarrantSigner.sign(w, kp.getPrivate()); }

    private static ISecurityWarrantKeyResolver resolver(String id, PublicKey key) {
        return keyId -> id.equals(keyId) ? (RSAPublicKey) key : null;
    }

    @Test void subjectRebindingIsRejected() throws Exception {
        var kp = rsa(); var w = sign(create(Instant.now(), "issuer", "alice", "api", null), kp);
        assertFalse(SecurityWarrantAuthorization.allows(w, "bob", "api", "Account.read", "read", "tenant-a", "account-1"));
    }

    @Test void audienceRebindingIsRejected() throws Exception {
        var kp = rsa(); var w = sign(create(Instant.now(), "issuer", "alice", "api-a", null), kp);
        assertThrows(IllegalStateException.class, () -> SecurityWarrantVerifier.verify(w, resolver(w.keyId(), kp.getPublic()), Instant.now(), "issuer", "api-b"));
    }

    @Test void tenantAndResourceConstraintsFailClosedWhenContextMissing() throws Exception {
        var kp = rsa();
        var c = new SecurityWarrantConstraints(List.of("tenant-a"), List.of(), List.of("account-1"), List.of(), null, null);
        var w = sign(create(Instant.now(), "issuer", "alice", "api", c), kp);
        assertFalse(SecurityWarrantAuthorization.allows(w, "alice", "api", "Account.read", "read", null, "account-1"));
        assertFalse(SecurityWarrantAuthorization.allows(w, "alice", "api", "Account.read", "read", "tenant-a", null));
    }

    @Test void singleByteSignatureMutationIsRejected() throws Exception {
        var kp = rsa(); var w = sign(create(Instant.now(), "issuer", "alice", "api", null), kp);
        byte[] sig = w.signature(); sig[sig.length / 2] ^= 1;
        var mutated = w.withSignature(sig);
        assertThrows(IllegalStateException.class, () -> SecurityWarrantVerifier.verify(mutated, resolver(w.keyId(), kp.getPublic()), Instant.now(), "issuer", "api"));
    }

    @Test void signedPayloadMutationIsRejected() throws Exception {
        var kp = rsa(); var w = sign(create(Instant.now(), "issuer", "alice", "api", null), kp);
        var mutated = new SecurityWarrant(w.id(), w.issuer(), "attacker", w.audience(), w.grants(), w.constraints(), w.issuedAt(), w.expiresAt(), w.nonce(), w.keyId(), w.parentId(), w.signature(), w.parentDigest(), w.delegationPath());
        assertThrows(IllegalStateException.class, () -> SecurityWarrantVerifier.verify(mutated, resolver(w.keyId(), kp.getPublic()), Instant.now(), "issuer", "api"));
    }

    @Test void keyIdentifierSubstitutionIsRejected() throws Exception {
        var kp = rsa(); var w = sign(create(Instant.now(), "issuer", "alice", "api", null), kp);
        var mutated = new SecurityWarrant(w.id(), w.issuer(), w.subject(), w.audience(), w.grants(), w.constraints(), w.issuedAt(), w.expiresAt(), w.nonce(), "attacker-key", w.parentId(), w.signature(), w.parentDigest(), w.delegationPath());
        assertThrows(IllegalStateException.class, () -> SecurityWarrantVerifier.verify(mutated, resolver(w.keyId(), kp.getPublic()), Instant.now(), "issuer", "api"));
    }

    @Test void algorithmSubstitutionCannotIntroduceUnknownScheme() {
        assertFalse(SecurityWarrantCanonicalizer.unsignedJson(create(Instant.now(), "issuer", "alice", "api", null)).toLowerCase().contains("algorithm"));
    }

    @Test void canonicalizationChangesWhenSecurityAuthorityChanges() {
        var now = Instant.now();
        var a = create(now, "issuer", "alice", "api", new SecurityWarrantConstraints(List.of("tenant-a"), List.of(), List.of(), List.of(), null, null));
        var b = new SecurityWarrant(a.id(), a.issuer(), a.subject(), a.audience(), a.grants(), new SecurityWarrantConstraints(List.of("tenant-b"), List.of(), List.of(), List.of(), null, null), a.issuedAt(), a.expiresAt(), a.nonce(), a.keyId(), a.parentId(), a.signature(), a.parentDigest(), a.delegationPath());
        assertNotEquals(a.digest(), b.digest());
    }

    private static KeyPair rsa() throws GeneralSecurityException { return KeyPairGenerator.getInstance("RSA").generateKeyPair(); }
}
