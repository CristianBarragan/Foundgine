package com.foundgine.providers.tools.mcp;

import static com.foundgine.providers.tools.mcp.McpAdapterParityTest.Engine;
import static com.foundgine.providers.tools.mcp.McpAdapterParityTest.failure;
import static com.foundgine.providers.tools.mcp.McpAdapterParityTest.host;
import static org.junit.jupiter.api.Assertions.*;

import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.execution.CancellationTokenSource;
import com.foundgine.core.semantic.SemanticContractSnapshot;
import com.foundgine.core.semantic.SemanticModelBuilder;
import com.foundgine.core.semantic.resolution.ISemanticLexicalCandidateSource;
import com.foundgine.core.semantic.resolution.SemanticLexicalCandidate;
import com.foundgine.core.semantic.resolution.SemanticLexicalReadIntentGrounder;
import com.foundgine.core.semantic.resolution.SemanticLexicalRequest;
import com.foundgine.core.semantic.resolution.SemanticLexicalResolver;
import com.foundgine.core.semantic.resolution.SemanticLexiconProjection;
import java.util.List;
import org.junit.jupiter.api.Test;

/** The open foundgine_query tool grounds natural language; foundgine_query_semantic never does. */
class McpLexicalGroundingParityTest {
    private static SemanticLexicalReadIntentGrounder grounder() {
        var model = new SemanticModelBuilder()
                .entity(new EntityId(1), "Supplier", e -> e.alias("Seller").identity(new FieldId(1), "Id"))
                .build().freeze();
        var contract = new SemanticContractSnapshot(model);
        var lexicon = SemanticLexiconProjection.build(contract);
        ISemanticLexicalCandidateSource source = new ISemanticLexicalCandidateSource() {
            @Override
            public List<SemanticLexicalCandidate> retrieve(SemanticLexicalRequest request) {
                return lexicon.stream()
                        .filter(x -> request.effectiveKinds().contains(x.kind()))
                        .filter(x -> x.canonicalName().equalsIgnoreCase(request.token())
                                || x.effectiveAliases().stream().anyMatch(a -> a.equalsIgnoreCase(request.token())))
                        .map(x -> new SemanticLexicalCandidate(request.token(), x.kind(), x.canonicalName(), .95, x.entityId(),
                                x.relationshipId(), x.fieldId(), x.sourceEntityId(), x.targetEntityId(), x.value(), List.of()))
                        .toList();
            }
        };
        return new SemanticLexicalReadIntentGrounder(contract, new SemanticLexicalResolver(contract, source));
    }

    @Test
    void naturalLanguageAndIntentPropertyAreGroundedAndGetHostSecurity() throws Exception {
        var e = new Engine();
        var ctx = host("alice");
        var tools = FoundgineMcpTools.builder(e.proxy).securityContextFactory(() -> ctx).lexicalGrounder(grounder()).build();

        tools.executeQueryAsync("supplier").toCompletableFuture().get();
        tools.executeQueryAsync("seller").toCompletableFuture().get();
        tools.executeQueryAsync("{\"intent\":\"seller\"}").toCompletableFuture().get();

        assertEquals(3, e.intents.size());
        for (var intent : e.intents) {
            assertNotNull(intent.rootEntity());
            assertSame(ctx, intent.security());
        }
        assertEquals(e.intents.get(0).rootEntity(), e.intents.get(1).rootEntity());
        assertEquals(e.intents.get(0).rootEntity(), e.intents.get(2).rootEntity());
    }

    @Test
    void structuredToolNeverGrounds() throws Exception {
        var e = new Engine();
        var tools = FoundgineMcpTools.builder(e.proxy).securityContextFactory(() -> host("a")).lexicalGrounder(grounder()).build();
        assertInstanceOf(IllegalArgumentException.class, failure(tools.executeSemanticQueryAsync("{\"intent\":\"supplier\"}")));
        assertInstanceOf(IllegalArgumentException.class, failure(tools.executeSemanticQueryAsync("supplier")));
        assertTrue(e.intents.isEmpty());
    }

    @Test
    void emptyIntentPropertyIsRejected() throws Exception {
        var tools = FoundgineMcpTools.builder(new Engine().proxy).securityContextFactory(() -> host("a")).lexicalGrounder(grounder()).build();
        assertInstanceOf(IllegalArgumentException.class, failure(tools.executeQueryAsync("{\"intent\":\"  \"}")));
    }

    @Test
    void cancelledTokenStopsGroundingBeforeExecution() throws Exception {
        var e = new Engine();
        var tools = FoundgineMcpTools.builder(e.proxy).securityContextFactory(() -> host("a")).lexicalGrounder(grounder()).build();
        var source = new CancellationTokenSource();
        source.cancel();
        // CompletableFuture.get() rethrows a CancellationException directly instead of wrapping it.
        var stage = tools.executeQueryAsync("supplier", source.token()).toCompletableFuture();
        assertThrows(java.util.concurrent.CancellationException.class, stage::get);
        assertTrue(e.intents.isEmpty());
    }
}
