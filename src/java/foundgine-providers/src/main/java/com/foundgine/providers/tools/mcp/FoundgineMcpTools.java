package com.foundgine.providers.tools.mcp;

import static com.foundgine.core.semantic.security.execution.SecurityExecutionContextProviderExtensions.requireSecurityExecutionContext;

import com.fasterxml.jackson.core.JsonProcessingException;
import com.fasterxml.jackson.databind.JsonNode;
import com.foundgine.core.execution.CancellationToken;
import com.foundgine.core.execution.ExecutionContext;
import com.foundgine.core.execution.ExecutionResult;
import com.foundgine.core.execution.ExecutionRow;
import com.foundgine.core.semantic.intent.ReadIntent;
import com.foundgine.core.semantic.resolution.SemanticLexicalReadIntentGrounder;
import com.foundgine.core.semantic.security.execution.DelegateSecurityExecutionContextProvider;
import com.foundgine.core.semantic.security.execution.ISecurityExecutionContextProvider;
import com.foundgine.core.semantic.security.execution.SecurityExecutionContext;
import com.foundgine.core.serialization.JsonReadIntentAdapter;
import com.foundgine.runtime.IFoundgine;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Objects;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.CompletionStage;
import java.util.function.Supplier;

/**
 * MCP transport adapter for the Foundgine semantic application contract (port of
 * {@code Foundgine.Providers.Tools.MCP.FoundgineMcpTools}). MCP arguments are translated into Foundgine inputs; MCP never owns
 * domain semantics, authorization, planning, or provider execution.
 *
 * <p>Tools: {@value FoundgineMcpToolNames#CAPABILITIES}, {@value FoundgineMcpToolNames#QUERY} (natural-language or JSON intent,
 * lexical grounding) and {@value FoundgineMcpToolNames#QUERY_SEMANTIC} (already-structured intent). Authentication, tenant and
 * authorization context are host-owned: they come only from the {@link ISecurityExecutionContextProvider}, are required before any
 * parsing or execution (fail closed), and replace whatever {@code security} a parsed intent carried. Async failures are reported
 * as failed stages (the C# {@code Task} faults); {@link #describeCapabilities()} throws synchronously, like the C# method.
 *
 * <p>The transport (HTTP, stdio) is the host's; {@link FoundgineMcpServer} is a ready JSON-RPC core for it.
 */
public final class FoundgineMcpTools {
    /** Legacy pass-through executor. Prefer the {@link IFoundgine} constructor: it enforces the host security boundary. */
    @Deprecated
    public interface Executor {
        CompletionStage<Object> execute(String intentJson);
    }

    private final IFoundgine foundgine;
    private final JsonReadIntentAdapter adapter;
    private final SemanticLexicalReadIntentGrounder lexicalGrounder;
    private final Supplier<ExecutionContext> contextFactory;
    private final ISecurityExecutionContextProvider securityContextProvider;
    private final Executor legacy;

    /**
     * @param foundgine the Foundgine execution engine (required)
     * @param adapter JSON read-intent adapter; {@code null} means a new default instance
     * @param contextFactory execution-context values per call; {@code null} means an empty context
     * @param securityContextProvider host-owned source of the caller's context; preferred over {@code securityContextFactory}
     * @param securityContextFactory delegate form of the provider; both may not be supplied together
     * @param lexicalGrounder grounds natural-language intents; {@code null} disables open lexical intents
     */
    public FoundgineMcpTools(
            IFoundgine foundgine,
            JsonReadIntentAdapter adapter,
            Supplier<ExecutionContext> contextFactory,
            ISecurityExecutionContextProvider securityContextProvider,
            Supplier<SecurityExecutionContext> securityContextFactory,
            SemanticLexicalReadIntentGrounder lexicalGrounder) {
        this.foundgine = Objects.requireNonNull(foundgine, "foundgine");
        this.adapter = adapter != null ? adapter : new JsonReadIntentAdapter();
        this.lexicalGrounder = lexicalGrounder;
        this.contextFactory = contextFactory != null ? contextFactory : () -> ExecutionContext.EMPTY;
        if (securityContextProvider != null && securityContextFactory != null)
            throw new IllegalArgumentException(
                    "Only one of securityContextProvider or securityContextFactory may be supplied.");
        this.securityContextProvider =
                securityContextProvider != null
                        ? securityContextProvider
                        : new DelegateSecurityExecutionContextProvider(
                                securityContextFactory != null ? securityContextFactory : () -> null);
        this.legacy = null;
    }

    /** @deprecated pass-through without the security boundary; kept for existing hosts and samples. */
    @Deprecated
    public FoundgineMcpTools(Executor executor) {
        this.legacy = Objects.requireNonNull(executor);
        this.foundgine = null;
        this.adapter = null;
        this.lexicalGrounder = null;
        this.contextFactory = null;
        this.securityContextProvider = null;
    }

    public static Builder builder(IFoundgine foundgine) { return new Builder(foundgine); }

    public static final class Builder {
        private final IFoundgine foundgine;
        private JsonReadIntentAdapter adapter;
        private Supplier<ExecutionContext> contextFactory;
        private ISecurityExecutionContextProvider provider;
        private Supplier<SecurityExecutionContext> factory;
        private SemanticLexicalReadIntentGrounder grounder;

        private Builder(IFoundgine foundgine) { this.foundgine = foundgine; }

        public Builder adapter(JsonReadIntentAdapter v) { adapter = v; return this; }
        public Builder contextFactory(Supplier<ExecutionContext> v) { contextFactory = v; return this; }
        public Builder securityContextProvider(ISecurityExecutionContextProvider v) { provider = v; return this; }
        public Builder securityContextFactory(Supplier<SecurityExecutionContext> v) { factory = v; return this; }
        public Builder lexicalGrounder(SemanticLexicalReadIntentGrounder v) { grounder = v; return this; }

        public FoundgineMcpTools build() {
            return new FoundgineMcpTools(foundgine, adapter, contextFactory, provider, factory, grounder);
        }
    }

    /** Canonical capability contract for the current caller. Discovery does not grant authorization. */
    public String describeCapabilities() {
        requireEngine();
        var security = requireSecurityExecutionContext(securityContextProvider, "MCP", "capability discovery");
        return writeValue(foundgine.describeCapabilityContract(security));
    }

    public CompletionStage<String> executeQueryAsync(String intentJson) {
        return executeQueryAsync(intentJson, CancellationToken.NONE);
    }

    /** Natural-language or JSON read intent (with lexical grounding); the host owns authentication, tenant and authorization. */
    public CompletionStage<String> executeQueryAsync(String intentJson, CancellationToken cancellationToken) {
        return guarded(() -> {
            requireEngine();
            if (intentJson == null || intentJson.isBlank()) throw new IllegalArgumentException("Intent JSON is required.");
            var security = requireSecurityExecutionContext(securityContextProvider, "MCP", "execution");
            return executeResolved(parseOpenIntent(intentJson, cancellationToken), security, cancellationToken);
        });
    }

    public CompletionStage<String> executeSemanticQueryAsync(String intentJson) {
        return executeSemanticQueryAsync(intentJson, CancellationToken.NONE);
    }

    /** Already-structured provider-neutral read intent. Does not invoke lexical grounding. */
    public CompletionStage<String> executeSemanticQueryAsync(String intentJson, CancellationToken cancellationToken) {
        return guarded(() -> {
            requireEngine();
            if (intentJson == null || intentJson.isBlank())
                throw new IllegalArgumentException("Semantic intent JSON is required.");
            var security = requireSecurityExecutionContext(securityContextProvider, "MCP", "structured execution");
            if (!readTree(intentJson).isObject()) throw new IllegalArgumentException("Structured intent must be a JSON object.");
            return executeResolved(parseIntent(intentJson), security, cancellationToken);
        });
    }

    /** @deprecated legacy pass-through; use {@link #executeQueryAsync(String)}. */
    @Deprecated
    public CompletionStage<String> foundgineQuery(String intentJson) {
        if (legacy == null) return executeQueryAsync(intentJson);
        return legacy.execute(intentJson).thenApply(FoundgineMcpTools::writeValue);
    }

    private CompletionStage<String> executeResolved(ReadIntent parsed, SecurityExecutionContext security, CancellationToken token) {
        // The host context always wins: a security block smuggled into the intent is overwritten, never trusted.
        var intent = new ReadIntent(parsed.rootEntity(), parsed.selections(), parsed.filter(), parsed.order(),
                parsed.limit(), parsed.offset(), parsed.after(), security);
        var context = contextFactory.get();
        return foundgine.executeAsync(intent, context != null ? context : ExecutionContext.EMPTY, token)
                .thenApply(FoundgineMcpTools::serialize);
    }

    private ReadIntent parseOpenIntent(String input, CancellationToken token) {
        var trimmed = input.strip();
        if (trimmed.startsWith("{")) {
            var root = readTree(trimmed);
            var expression = root.get("intent");
            if (expression != null && expression.isTextual()) {
                if (expression.asText().isBlank()) throw new IllegalArgumentException("'intent' cannot be empty.");
                return groundLexically(expression.asText(), token);
            }
            return parseIntent(trimmed);
        }
        return groundLexically(trimmed, token);
    }

    /** The core adapter rejects malformed intents with IllegalStateException; surface them as agent-visible validation errors. */
    private ReadIntent parseIntent(String json) {
        try {
            return adapter.parse(json);
        } catch (IllegalStateException e) {
            throw new IllegalArgumentException(e.getMessage(), e);
        }
    }

    private ReadIntent groundLexically(String expression, CancellationToken token) {
        if (lexicalGrounder == null)
            throw new FoundgineMcpNotConfiguredException(
                    "Open lexical intents are not configured. Enable Foundgine grounding and register an ISemanticLexicalCandidateSource.");
        token.throwIfCancellationRequested();
        return lexicalGrounder.ground(expression, token::isCancellationRequested);
    }

    private static String serialize(ExecutionResult result) {
        var out = new LinkedHashMap<String, Object>();
        List<ExecutionRow> rows = result.rows() == null ? List.of() : result.rows();
        out.put("rows", rows.stream().map(ExecutionRow::values).toList());
        out.put("pageInfo", result.pageInfo());
        out.put("evidence", result.evidence());
        out.put("receipt", result.receipt());
        return writeValue(out);
    }

    private static JsonNode readTree(String json) {
        try {
            var node = FoundgineMcpJson.READER.readTree(json);
            if (node == null || node.isMissingNode()) throw new IllegalArgumentException("Intent JSON is empty.");
            return node;
        } catch (JsonProcessingException e) {
            throw new IllegalArgumentException("Intent JSON is not valid JSON.", e);
        }
    }

    private static String writeValue(Object value) {
        try {
            return FoundgineMcpJson.WRITER.writeValueAsString(value);
        } catch (JsonProcessingException e) {
            throw new IllegalStateException("Unable to serialize the MCP tool result.", e);
        }
    }

    private void requireEngine() {
        if (foundgine == null)
            throw new FoundgineMcpNotConfiguredException("This FoundgineMcpTools was created without an IFoundgine engine.");
    }

    private static CompletionStage<String> guarded(Supplier<CompletionStage<String>> body) {
        try {
            return body.get();
        } catch (RuntimeException e) {
            return CompletableFuture.failedFuture(e);
        }
    }
}
