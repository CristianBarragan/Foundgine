package com.foundgine.extensions.graphql.http;

import com.fasterxml.jackson.core.type.TypeReference;
import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.execution.CancellationToken;
import com.foundgine.core.execution.ExecutionCellKey;
import com.foundgine.core.execution.ExecutionRow;
import com.foundgine.core.semantic.RelationshipCardinality;
import com.foundgine.core.semantic.SemanticModel;
import com.foundgine.extensions.graphql.FoundgineGraphQLMutationExecutor;
import com.foundgine.extensions.graphql.FoundgineGraphQLQueryExecutor;
import com.foundgine.extensions.graphql.GraphQLAdapterError;
import com.foundgine.extensions.graphql.GraphQLAdapterErrorCode;
import com.foundgine.extensions.graphql.GraphQLAdapterErrors;
import com.foundgine.extensions.graphql.GraphQLResultShape;
import com.foundgine.extensions.graphql.GraphQLResultShape.GraphQLResultField;
import com.sun.net.httpserver.HttpExchange;
import com.sun.net.httpserver.HttpHandler;
import com.sun.net.httpserver.HttpServer;
import graphql.language.Document;
import graphql.language.Field;
import graphql.language.OperationDefinition;
import graphql.parser.Parser;
import java.io.IOException;
import java.math.BigDecimal;
import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Objects;
import java.util.Set;
import java.util.concurrent.CompletionException;
import java.util.function.Function;

/**
 * HTTP front door for Foundgine GraphQL on the JDK's built-in {@code com.sun.net.httpserver}: no web framework needed.
 * Port of the C# {@code GraphQLHttpEndpoint}: bearer-authenticated POST, parse, host authorization hook, root-field
 * role check, then the secure {@link FoundgineGraphQLQueryExecutor} (or mutation executor), with the flat row/cell
 * result re-assembled into the nested GraphQL shape. Missing or invalid token -> HTTP 401; authenticated but not
 * permitted -> a GraphQL error with HTTP 200, as in C#.
 *
 * <p>The handler blocks on the executor on the request thread and publishes the caller through
 * {@link #currentPrincipal()} for the duration of the call, so a host {@code ISecurityExecutionContextProvider} or
 * authorization policy can read it the way the C# host reads {@code IHttpContextAccessor}. This relies on the ORM
 * provider executing on the calling thread, which {@code OrmExecutionProvider} does; give the {@link HttpServer} a
 * thread-pool executor, since the default executor serves one request at a time.
 *
 * <p>UNVERIFIED: written without a Maven build or package access; not compiled against the real jars or run.
 */
public final class GraphQLHttpHandler implements HttpHandler {
    private static final int MAX_BODY_BYTES = 1 << 20;
    private static final ThreadLocal<Principal> CURRENT = new ThreadLocal<>();

    /** The authenticated caller. Claims must come from the validated token, never from a GraphQL argument. */
    public record Principal(String subject, Set<String> roles, Map<String, String> claims) {
        public Principal {
            roles = Set.copyOf(roles);
            claims = Map.copyOf(claims);
        }

        public boolean isInRole(String role) {
            return roles.contains(role);
        }

        public String claim(String name) {
            return claims.get(name);
        }
    }

    /** What a host {@link Options#authorize} rule sees after parsing and before execution. */
    public record OperationContext(
            Principal user, OperationDefinition.Operation operation, List<Field> rootFields, Map<String, Object> variables) {}

    /** Validates a bearer token; returns the principal, or null when the token is missing, invalid or expired. */
    @FunctionalInterface
    public interface Authenticator {
        Principal authenticate(String bearerToken);
    }

    /** Handler configuration. Fail closed: every query root field and the roles allowed to START there must be listed. */
    public static final class Options {
        /** Route of the POST endpoint. Default {@code /graphql}. */
        public String path = "/graphql";

        /**
         * Query root field -> roles allowed to START a query there. A root field that is not listed is forbidden, so at
         * least one entry is required. Stops a role that holds a read capability only so related rows can be included
         * (order lines under an order) from starting at that entity.
         */
        public final Map<String, Set<String>> queryRootRoles = new LinkedHashMap<>();

        /** Accept mutation operations (needs a mutation executor). Default false. */
        public boolean allowMutations;

        /**
         * Optional host rule run after parsing and before any root-role check or execution. Return an error to refuse the
         * request, or null to continue.
         */
        public Function<OperationContext, GraphQLAdapterError> authorize;
    }

    private final FoundgineGraphQLQueryExecutor queries;
    private final FoundgineGraphQLMutationExecutor mutations;
    private final SemanticModel model;
    private final Authenticator authenticator;
    private final String path;
    private final Map<String, Set<String>> queryRootRoles;
    private final boolean allowMutations;
    private final Function<OperationContext, GraphQLAdapterError> authorize;
    private final ObjectMapper json = new ObjectMapper().findAndRegisterModules();

    /** {@code mutations} may be null when {@link Options#allowMutations} is false. */
    public GraphQLHttpHandler(
            FoundgineGraphQLQueryExecutor queries,
            FoundgineGraphQLMutationExecutor mutations,
            SemanticModel model,
            Authenticator authenticator,
            Options options) {
        this.queries = Objects.requireNonNull(queries, "queries");
        this.model = Objects.requireNonNull(model, "model");
        this.authenticator = Objects.requireNonNull(authenticator, "authenticator");
        Objects.requireNonNull(options, "options");
        if (options.queryRootRoles.isEmpty())
            throw new IllegalStateException("Options.queryRootRoles is empty: the handler fails closed, so list each query"
                    + " root field and the roles allowed to start there.");
        if (options.path == null || options.path.isBlank())
            throw new IllegalStateException("Options.path must not be empty.");
        if (options.allowMutations && mutations == null)
            throw new IllegalStateException("Options.allowMutations needs a mutation executor.");
        this.mutations = mutations;
        // Snapshot: later changes to the options object must not alter authorization of a mounted handler.
        this.path = options.path;
        this.queryRootRoles = Map.copyOf(options.queryRootRoles);
        this.allowMutations = options.allowMutations;
        this.authorize = options.authorize;
    }

    /** Mounts this handler on {@code server} at the configured path. */
    public GraphQLHttpHandler register(HttpServer server) {
        server.createContext(path, this);
        return this;
    }

    /** The caller of the request being served on this thread, or null outside a request. */
    public static Principal currentPrincipal() {
        return CURRENT.get();
    }

    /** A scope opened by {@link #callAs}; closing it clears the current caller. */
    public interface Scope extends AutoCloseable {
        @Override
        void close();
    }

    /**
     * Publishes {@code principal} as this thread's caller until the returned scope is closed: for hosts that execute
     * Foundgine outside {@link #handle} (tests, other transports) but reuse an authorization policy or security-context
     * provider written against {@link #currentPrincipal()}.
     */
    public static Scope callAs(Principal principal) {
        CURRENT.set(Objects.requireNonNull(principal, "principal"));
        return CURRENT::remove;
    }

    @Override
    public void handle(HttpExchange exchange) throws IOException {
        try (exchange) {
            if (!"POST".equals(exchange.getRequestMethod())) {
                exchange.getResponseHeaders().set("Allow", "POST");
                send(exchange, 405, Map.of("errors", List.of(errorBody("Only POST is supported.", "BAD_REQUEST"))));
                return;
            }
            var principal = authenticate(exchange);
            if (principal == null) {
                exchange.getResponseHeaders().set("WWW-Authenticate", "Bearer");
                exchange.sendResponseHeaders(401, -1);
                return;
            }
            CURRENT.set(principal);
            try {
                send(exchange, 200, process(exchange, principal));
            } finally {
                CURRENT.remove();
            }
        }
    }

    private Principal authenticate(HttpExchange exchange) {
        var header = exchange.getRequestHeaders().getFirst("Authorization");
        if (header == null || header.length() < 8 || !header.regionMatches(true, 0, "Bearer ", 0, 7)) return null;
        try {
            return authenticator.authenticate(header.substring(7).trim());
        } catch (RuntimeException invalidToken) {
            return null;
        }
    }

    private Map<String, Object> process(HttpExchange exchange, Principal principal) throws IOException {
        byte[] body = exchange.getRequestBody().readNBytes(MAX_BODY_BYTES + 1);
        if (body.length > MAX_BODY_BYTES) return error("The GraphQL request body is too large.", "BAD_REQUEST");

        JsonNode request;
        try {
            request = json.readTree(body);
        } catch (IOException notJson) {
            return error("The GraphQL request body is not valid JSON.", "BAD_REQUEST");
        }
        if (request == null || !request.isObject() || !request.hasNonNull("query") || request.get("query").asText().isBlank())
            return error("A GraphQL query is required.", "BAD_REQUEST");
        String query = request.get("query").asText();
        String operationName = request.hasNonNull("operationName") ? request.get("operationName").asText() : null;

        Map<String, Object> variables;
        var variableNode = request.get("variables");
        if (variableNode == null || variableNode.isNull()) variables = Map.of();
        else if (!variableNode.isObject()) return error("GraphQL variables must be a JSON object.", "BAD_USER_INPUT");
        else variables = json.convertValue(variableNode, new TypeReference<LinkedHashMap<String, Object>>() {});

        OperationDefinition.Operation operation;
        List<Field> rootFields;
        Field root;
        try {
            Document document = new Parser().parseDocument(query);
            var operations = document.getDefinitionsOfType(OperationDefinition.class);
            OperationDefinition selected;
            if (operationName == null) {
                if (operations.size() != 1)
                    throw new IllegalStateException("An operationName is required when a document contains multiple operations.");
                selected = operations.get(0);
            } else {
                final String wanted = operationName;
                selected = operations.stream().filter(o -> wanted.equals(o.getName())).findFirst().orElseThrow(
                        () -> new IllegalStateException("GraphQL operation '" + wanted + "' was not found."));
            }
            operation = selected.getOperation();
            rootFields = new ArrayList<>();
            for (var selection : selected.getSelectionSet().getSelections())
                if (selection instanceof Field f && !f.getName().startsWith("__")) rootFields.add(f);
            if (rootFields.isEmpty()) throw new IllegalStateException("The GraphQL operation has no executable root field.");
            root = rootFields.get(0);
        } catch (RuntimeException invalid) {
            return error(GraphQLAdapterErrors.fromException(invalid));
        }

        if (authorize != null) {
            var denied = authorize.apply(new OperationContext(principal, operation, List.copyOf(rootFields), variables));
            if (denied != null) return error(denied);
        }

        if (operation == OperationDefinition.Operation.QUERY) {
            for (var rootField : rootFields) {
                var allowed = queryRootRoles.get(rootField.getName());
                if (allowed == null || allowed.stream().noneMatch(principal::isInRole))
                    return error(new GraphQLAdapterError(GraphQLAdapterErrorCode.FORBIDDEN,
                            "The caller is not authorized to query '" + rootField.getName() + "'.", null,
                            GraphQLAdapterErrorCode.FORBIDDEN));
            }
        }

        try {
            if (operation == OperationDefinition.Operation.MUTATION && allowMutations) {
                var result = mutations.tryExecuteAsync(query, variables, operationName, CancellationToken.NONE)
                        .toCompletableFuture().join();
                if (!result.succeeded()) return error(result.error());
                return Map.of("data", result.data().data());
            }
            if (operation != OperationDefinition.Operation.QUERY)
                return error(new GraphQLAdapterError(GraphQLAdapterErrorCode.GRAPHQL_VALIDATION_FAILED,
                        allowMutations ? "Only query and mutation operations are supported." : "Only query operations are supported."));

            var result = queries.tryExecuteAsync(query, variables, operationName, CancellationToken.NONE)
                    .toCompletableFuture().join();
            if (!result.succeeded()) return error(result.error());

            // Re-assemble the provider-neutral flat row/cell representation into the nested GraphQL selection tree. Node ids
            // are assigned preorder by the core SemanticRequestResolver; relationship collections are de-duplicated by identity.
            final String rootName = root.getName();
            var rootEntity = model.entities().stream().filter(e -> e.name().equalsIgnoreCase(rootName)).findFirst().orElse(null);
            if (rootEntity == null)
                return error(new GraphQLAdapterError(GraphQLAdapterErrorCode.ADAPTER_ERROR,
                        "The GraphQL root entity is not present in the semantic model."));
            var projection = QueryProjection.build(model, rootEntity.id(), result.data().resultShape());
            var rows = QueryProjection.materialize(result.data().execution().rows(), projection);
            String responseName = root.getAlias() != null ? root.getAlias() : rootName;
            var data = new LinkedHashMap<String, Object>();
            data.put(responseName, rows);
            return Map.of("data", data);
        } catch (CompletionException failed) {
            return error(GraphQLAdapterErrors.fromException(
                    failed.getCause() instanceof Exception e ? e : new IllegalStateException(failed.getCause())));
        }
    }

    private static Map<String, Object> error(GraphQLAdapterError e) {
        return Map.of("errors", List.of(errorBody(e.message(), e.code())));
    }

    private static Map<String, Object> error(String message, String code) {
        return Map.of("errors", List.of(errorBody(message, code)));
    }

    private static Map<String, Object> errorBody(String message, String code) {
        return Map.of("message", message == null ? "" : message, "extensions", Map.of("code", code));
    }

    private void send(HttpExchange exchange, int status, Map<String, Object> body) throws IOException {
        byte[] bytes = json.writeValueAsBytes(body);
        exchange.getResponseHeaders().set("Content-Type", "application/json");
        exchange.sendResponseHeaders(status, bytes.length);
        exchange.getResponseBody().write(bytes);
    }

    /** Rebuilds the nested GraphQL result from flat rows. Package-private so the tests can drive it directly. */
    static final class QueryProjection {
        record Child(GraphQLResultField field, Node node, boolean isMany) {}

        record Node(int id, EntityId entity, FieldId identityField, GraphQLResultShape shape, List<Child> children) {}

        private static final class Accumulator {
            final Node node;
            final Map<String, Object> scalars = new LinkedHashMap<>();
            final Map<String, Map<String, Accumulator>> many = new LinkedHashMap<>();
            final Map<String, Accumulator> one = new LinkedHashMap<>();

            Accumulator(Node node) {
                this.node = node;
            }
        }

        private QueryProjection() {}

        static Node build(SemanticModel model, EntityId entity, GraphQLResultShape shape) {
            return buildNode(model, entity, shape, new int[] {0});
        }

        private static Node buildNode(SemanticModel model, EntityId entity, GraphQLResultShape shape, int[] nextId) {
            int id = nextId[0]++;
            var children = new ArrayList<Child>();
            var metadata = model.get(entity);
            for (var field : shape.fields()) {
                if (field.relationship() == null) continue;
                var relationship = metadata.relationships().stream()
                        .filter(r -> r.id().equals(field.relationship())).findFirst().orElseThrow();
                var childShape = field.children() != null ? field.children() : new GraphQLResultShape(List.of());
                children.add(new Child(field, buildNode(model, relationship.target(), childShape, nextId),
                        relationship.cardinality() == RelationshipCardinality.MANY));
            }
            return new Node(id, entity, metadata.identity().fieldId(), shape, children);
        }

        static List<Object> materialize(List<ExecutionRow> rows, Node root) {
            var roots = new LinkedHashMap<String, Accumulator>();
            int rowNumber = 0;
            for (var row : rows) {
                Object key = readIdentity(row, root);
                String keyText = key == null ? "row:" + rowNumber : keyText(key);
                merge(row, roots.computeIfAbsent(keyText, k -> new Accumulator(root)));
                rowNumber++;
            }
            var out = new ArrayList<Object>(roots.size());
            for (var accumulator : roots.values()) out.add(toValue(accumulator));
            return out;
        }

        private static void merge(ExecutionRow row, Accumulator accumulator) {
            var node = accumulator.node;
            var cells = row.effectiveCells();
            for (var field : node.shape().fields()) {
                if (field.field() == null) continue;
                var cell = new ExecutionCellKey(node.id(), node.entity(), field.field());
                if (cells.containsKey(cell)) accumulator.scalars.put(field.alias(), cells.get(cell));
            }
            for (var child : node.children()) {
                String alias = child.field().alias();
                if (child.isMany()) accumulator.many.computeIfAbsent(alias, a -> new LinkedHashMap<>());
                else accumulator.one.putIfAbsent(alias, null);

                Object key = readIdentity(row, child.node());
                if (key == null) continue; // left-joined child absent
                String keyText = keyText(key);
                Accumulator childAccumulator;
                if (child.isMany()) {
                    childAccumulator = accumulator.many.get(alias).computeIfAbsent(keyText, k -> new Accumulator(child.node()));
                } else {
                    childAccumulator = accumulator.one.get(alias);
                    if (childAccumulator == null) {
                        childAccumulator = new Accumulator(child.node());
                        accumulator.one.put(alias, childAccumulator);
                    }
                }
                merge(row, childAccumulator);
            }
        }

        private static Object readIdentity(ExecutionRow row, Node node) {
            return row.effectiveCells().get(new ExecutionCellKey(node.id(), node.entity(), node.identityField()));
        }

        private static Object toValue(Accumulator accumulator) {
            var result = new LinkedHashMap<String, Object>(accumulator.scalars);
            for (var e : accumulator.many.entrySet()) {
                var list = new ArrayList<Object>(e.getValue().size());
                for (var child : e.getValue().values()) list.add(toValue(child));
                result.put(e.getKey(), list);
            }
            for (var e : accumulator.one.entrySet())
                result.put(e.getKey(), e.getValue() == null ? null : toValue(e.getValue()));
            return result;
        }

        /** Identity text that is stable across Integer/Long/BigDecimal representations of the same number. */
        private static String keyText(Object value) {
            return value instanceof BigDecimal b ? b.stripTrailingZeros().toPlainString() : String.valueOf(value);
        }
    }
}
