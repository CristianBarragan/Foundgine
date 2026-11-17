package com.foundgine.benchmark;

import com.foundgine.core.semantic.security.warrants.ISecurityWarrantRevocationStore;
import jakarta.servlet.FilterChain;
import jakarta.servlet.ServletException;
import jakarta.servlet.http.HttpServletRequest;
import jakarta.servlet.http.HttpServletResponse;
import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.util.HexFormat;
import org.springframework.security.core.Authentication;
import org.springframework.security.oauth2.server.resource.authentication.JwtAuthenticationToken;
import org.springframework.web.filter.OncePerRequestFilter;

/** Applies the same shared revocation-state gate to each authenticated benchmark GraphQL request. */
public final class BenchmarkRevocationFilter extends OncePerRequestFilter {
    private final ISecurityWarrantRevocationStore revocations;
    public BenchmarkRevocationFilter(ISecurityWarrantRevocationStore revocations) { this.revocations = revocations; }
    @Override protected boolean shouldNotFilter(HttpServletRequest request) { return !request.getRequestURI().startsWith("/graphql"); }
    @Override protected void doFilterInternal(HttpServletRequest request, HttpServletResponse response, FilterChain chain)
            throws ServletException, IOException {
        Authentication authentication = org.springframework.security.core.context.SecurityContextHolder.getContext().getAuthentication();
        if (!(authentication instanceof JwtAuthenticationToken jwt)) { response.sendError(401); return; }
        try {
            String digest = HexFormat.of().withUpperCase().formatHex(MessageDigest.getInstance("SHA-256").digest(jwt.getToken().getTokenValue().getBytes(StandardCharsets.UTF_8)));
            String warrantId = jwt.getToken().getId() == null ? jwt.getName() : jwt.getToken().getId();
            if (revocations instanceof com.foundgine.core.semantic.security.warrants.IBatchedSecurityWarrantRevocationStore batched) {
                // One consistent snapshot: no separate before/after sequence bracket is needed.
                var lookup = batched.lookup(warrantId, digest, java.util.List.of());
                if (lookup.revoked()) { response.sendError(401, "Bearer token revoked"); return; }
                chain.doFilter(request, response);
                return;
            }
            long before = revocations.currentSequence();
            boolean revokedByIdentity = revocations.isRevoked(warrantId, digest);
            boolean revokedByDigest = revocations.isDigestRevoked(digest);
            long after = revocations.currentSequence();
            if (revokedByIdentity || revokedByDigest) { response.sendError(401, "Bearer token revoked"); return; }
            if (after != before) { response.sendError(503, "Revocation state changed during validation"); return; }
            chain.doFilter(request, response);
        } catch (RuntimeException | java.security.NoSuchAlgorithmException failure) {
            response.sendError(503, "Revocation state unavailable; failing closed");
        }
    }
}
