package com.foundgine.core.semantic.security.warrants;

import static org.junit.jupiter.api.Assertions.assertEquals;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import java.io.File;
import java.math.BigDecimal;
import java.nio.file.Files;
import java.time.Instant;
import java.time.OffsetDateTime;
import java.util.ArrayList;
import java.util.Collection;
import java.util.List;
import java.util.stream.Stream;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.MethodSource;

/**
 * Cross-language conformance for delegation: attenuation and chain validation must accept and reject exactly the same
 * scenarios, with the same exception message, as the C# test of the same name. Vectors:
 * src/conformance/delegation-scenarios.json. Override the directory with -Dfoundgine.conformance.dir=...
 */
class WarrantDelegationConformanceTest {
    private static final ObjectMapper MAPPER = new ObjectMapper();

    static Stream<JsonNode> scenarios() throws Exception {
        String dir = System.getProperty("foundgine.conformance.dir");
        File f = new File(dir != null ? dir : "../../conformance", "delegation-scenarios.json");
        if (!f.isFile()) throw new IllegalStateException("Conformance scenarios not found: " + f.getAbsolutePath());
        List<JsonNode> all = new ArrayList<>();
        MAPPER.readTree(Files.readAllBytes(f.toPath())).get("scenarios").forEach(all::add);
        return all.stream();
    }

    private static List<String> strings(JsonNode n) {
        List<String> out = new ArrayList<>();
        if (n != null && !n.isNull()) n.forEach(x -> out.add(x.asText()));
        return out;
    }

    private static String text(JsonNode n) {
        return n == null || n.isNull() ? null : n.asText();
    }

    private static SecurityWarrant build(JsonNode w) {
        List<CapabilityGrant> grants = new ArrayList<>();
        w.get("grants")
                .forEach(g -> grants.add(new CapabilityGrant(
                        g.get("capability").asText(),
                        g.get("operation").asText(),
                        (Collection<String>) strings(g.get("resourceScopes")))));
        JsonNode c = w.get("constraints");
        var constraints = new SecurityWarrantConstraints(
                (Collection<String>) strings(c.get("allowedTenants")),
                (Collection<String>) strings(c.get("allowedFields")),
                (Collection<String>) strings(c.get("resourceScopes")),
                (Collection<String>) strings(c.get("allowedOperations")),
                c.hasNonNull("maxResults") ? c.get("maxResults").asLong() : null,
                c.hasNonNull("maxAmount") ? new BigDecimal(c.get("maxAmount").asText()) : null);
        return new SecurityWarrant(
                w.get("id").asText(), w.get("issuer").asText(), w.get("subject").asText(), w.get("audience").asText(),
                grants, constraints,
                OffsetDateTime.parse(w.get("issuedAt").asText()).toInstant(),
                OffsetDateTime.parse(w.get("expiresAt").asText()).toInstant(),
                w.get("nonce").asText(), w.get("keyId").asText(), text(w.get("parentId")), new byte[0],
                text(w.get("parentDigest")), strings(w.get("delegationPath")));
    }

    @ParameterizedTest
    @MethodSource("scenarios")
    void behavesLikeTheReferenceScenario(JsonNode scenario) {
        String name = scenario.get("name").asText();
        List<SecurityWarrant> warrants = new ArrayList<>();
        scenario.get("warrants").forEach(w -> warrants.add(build(w)));
        List<String> digests = strings(scenario.get("digests"));
        for (int i = 0; i < warrants.size(); i++) assertEquals(digests.get(i), warrants.get(i).digest(), name + " digest #" + i);

        Instant now = OffsetDateTime.parse(scenario.get("now").asText()).toInstant();
        String error = null;
        try {
            if (scenario.get("op").asText().equals("attenuate"))
                SecurityWarrantAttenuator.attenuate(warrants.get(0), warrants.get(1), now);
            else SecurityWarrantDelegationChainValidator.validate(warrants, now, text(scenario.get("expectedRootDigest")));
        } catch (IllegalStateException e) {
            error = e.getMessage();
        }
        assertEquals(text(scenario.get("error")), error, name);
        if (scenario.hasNonNull("chainDigest"))
            assertEquals(scenario.get("chainDigest").asText(), SecurityWarrantDelegationChainValidator.chainDigest(warrants), name);
    }
}
