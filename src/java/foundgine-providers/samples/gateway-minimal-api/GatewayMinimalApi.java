package com.foundgine.providers.gateway;

import com.fasterxml.jackson.databind.ObjectMapper;
import com.foundgine.core.semantic.security.execution.ISecurityExecutionContextProvider;
import com.foundgine.core.semantic.security.execution.SecurityExecutionContext;
import com.foundgine.core.semantic.security.warrants.SecurityWarrant;
import com.foundgine.core.semantic.security.warrants.SecurityWarrantConstraints;
import com.foundgine.providers.eventstore.PostgresEventStore;
import com.foundgine.providers.eventstore.PostgresGatewayEventStore;
import com.foundgine.providers.eventstore.PostgresGatewayEventStoreOptions;
import com.foundgine.providers.eventstore.schema.EventStoreMigrator;
import com.foundgine.providers.gateway.approvals.FoundgineGatewayApprovalService;
import com.foundgine.providers.gateway.approvals.InMemoryFoundgineGatewayApprovalStore;
import com.foundgine.providers.gateway.events.FoundgineGatewayEventHub;
import com.foundgine.providers.gateway.events.InMemoryFoundgineGatewayEventStore;
import com.foundgine.providers.gateway.events.IFoundgineGatewayEventStore;
import com.foundgine.providers.gateway.execution.FoundgineGatewayExecution;
import com.foundgine.providers.gateway.http.FoundgineGatewayHttpServices;
import com.foundgine.providers.gateway.http.FoundgineGatewayRoutes;
import com.foundgine.providers.gateway.http.GatewayHttpAuthorizer;
import com.foundgine.providers.gateway.http.GatewayHttpRequest;
import com.foundgine.providers.gateway.http.GatewayHttpRoute;
import com.foundgine.providers.gateway.http.GatewayHttpRouter;
import com.foundgine.providers.gateway.http.GatewayJdkHttpServer;
import com.foundgine.providers.gateway.semantics.FoundgineGatewaySemanticDescription;
import com.foundgine.providers.gateway.semantics.FoundgineGatewaySemanticSource;
import com.foundgine.providers.gateway.semantics.IFoundgineGatewaySemanticSource;
import com.foundgine.runtime.IFoundgine;
import com.foundgine.runtime.IFoundgineExecutor;
import com.foundgine.runtime.IFoundgineMutations;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.time.Instant;
import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.concurrent.CountDownLatch;

/**
 * Java counterpart of the C# {@code Gateway.MinimalApi} sample: one host composing the Gateway HTTP facade, an optional
 * PostgreSQL event journal, and capability/provider discovery, served by the JDK's built-in {@code HttpServer}.
 *
 * <p>Like the C# sample it is a <em>composition</em> sample. The semantic model, the Runtime facade
 * ({@link IFoundgine}, {@link IFoundgineExecutor}, {@link IFoundgineMutations}) and real authentication are
 * application-owned; this sample does not fabricate them. Until you register them in {@link ApplicationRuntime}, the
 * discovery, health and event endpoints work, and execution endpoints fail with a 500 whose cause is logged on stderr.
 *
 * <p>Configuration (environment variables; the Java names of the C# configuration keys):
 * <pre>
 * FOUNDGINE_PORT                       port to listen on (default 8080; 0 picks a free port)
 * FOUNDGINE_HOST                       interface to bind (default 127.0.0.1)
 * FOUNDGINE_GATEWAY_NAME               Foundgine:GatewayName (default "Foundgine Minimal API")
 * FOUNDGINE_POSTGRES_CONNECTION_STRING ConnectionStrings:Postgres, jdbc: URL or Host=..;Database=.. form;
 *                                      enables the durable event journal and applies the event-store migrations
 * FOUNDGINE_SAMPLE_DEV_TOKEN           DEV ONLY bearer token. Without it every protected route answers 401.
 * FOUNDGINE_SAMPLE_DEV_SUBJECT         subject for the dev token (default "dev-user")
 * FOUNDGINE_SAMPLE_DEV_TENANT          tenant for the dev token (default "dev-tenant")
 * </pre>
 *
 * <p>Run (from the repository root, after building the modules; adjust the classpath to your build output):
 * <pre>
 * java -cp "&lt;core&gt;:&lt;runtime&gt;:&lt;providers&gt;:jackson-*.jar:postgresql.jar" \
 *      foundgine-providers/samples/gateway-minimal-api/GatewayMinimalApi.java
 * </pre>
 */
public final class GatewayMinimalApi {
    private GatewayMinimalApi() { }

    /**
     * Application-owned registrations: the equivalent of the C# README's "Application-owned registrations". Set these
     * from your application's generated metadata/model before {@link #main}; they are deliberately {@code null} here.
     */
    static final class ApplicationRuntime {
        static IFoundgine foundgine = null;
        static IFoundgineExecutor executor = null;
        static IFoundgineMutations mutations = null;

        private ApplicationRuntime() { }
    }

    public static void main(String[] args) throws Exception {
        var env = System.getenv();
        var gatewayName = env.getOrDefault("FOUNDGINE_GATEWAY_NAME", "Foundgine Minimal API");
        var host = env.getOrDefault("FOUNDGINE_HOST", "127.0.0.1");
        var port = Integer.parseInt(env.getOrDefault("FOUNDGINE_PORT", "8080"));

        // ---- gateway options and capabilities ---------------------------------------------------------------
        var options = new FoundgineGatewayOptions().name(gatewayName).contractVersion("1.1")
                .addCapability(new FoundgineGatewayCapability("semantic.read", "Semantic read", "1.0", "query",
                        "Execute authorized provider-neutral read intents.", List.of("POST /foundgine/read"), Map.of()));

        // ---- event journal: PostgreSQL when configured, otherwise in-memory (development / single instance) ---
        var providers = new ArrayList<IFoundgineProvider>();
        IFoundgineGatewayEventStore journal;
        var postgresUrl = resolvePostgresUrl(env.get("FOUNDGINE_POSTGRES_CONNECTION_STRING"));
        if (postgresUrl != null) {
            var dataSource = new org.postgresql.ds.PGSimpleDataSource();
            dataSource.setUrl(postgresUrl);
            EventStoreMigrator.apply(dataSource); // migrations run before serving traffic, as in the C# sample
            var events = new PostgresEventStore(dataSource);
            var gatewayJournal = new PostgresGatewayEventStore(events, new PostgresGatewayEventStoreOptions());
            providers.add(events);
            providers.add(gatewayJournal);
            journal = gatewayJournal;
        } else {
            journal = new InMemoryFoundgineGatewayEventStore();
            providers.add((IFoundgineProvider) journal);
        }
        var hub = new FoundgineGatewayEventHub(options, List.of(), journal);
        // Negotiate before serving traffic. The in-memory journal is sufficient for local replay; a
        // PostgreSQL-configured host additionally requires durable storage from one provider.
        var providerRegistry = new FoundgineGatewayProviderRegistry(hub);
        var providerRequirements = new ArrayList<FoundgineProviderRequirement>();
        providerRequirements.add(new FoundgineProviderRequirement(FoundgineProviderKind.EVENT_STORE,
                FoundgineProviderCapability.mask(FoundgineProviderCapability.ORDERED_EVENTS,
                        FoundgineProviderCapability.EVENT_REPLAY), "Gateway journal must support ordered replay."));
        if (postgresUrl != null) providerRequirements.add(new FoundgineProviderRequirement(
                FoundgineProviderKind.EVENT_STORE,
                FoundgineProviderCapability.mask(FoundgineProviderCapability.DURABLE_STORAGE),
                "PostgreSQL journal was configured; durable event storage is required."));
        FoundgineProviderStartup.validateAndRegister(providers, providerRequirements, journal, providerRegistry);
        var gateway = new FoundgineGateway(options, hub);

        // ---- trusted security context: set by the authorizer, cleared after every request -------------------
        var requestContext = new ThreadLocal<SecurityExecutionContext>();
        ISecurityExecutionContextProvider security = requestContext::get;
        var authorizer = new DevTokenAuthorizer(env.get("FOUNDGINE_SAMPLE_DEV_TOKEN"),
                env.getOrDefault("FOUNDGINE_SAMPLE_DEV_SUBJECT", "dev-user"),
                env.getOrDefault("FOUNDGINE_SAMPLE_DEV_TENANT", "dev-tenant"), requestContext);

        // ---- execution, approvals (in-memory: development / single instance only) ---------------------------
        var execution = new FoundgineGatewayExecution(
                ApplicationRuntime.executor != null ? ApplicationRuntime.executor : notRegistered(IFoundgineExecutor.class),
                ApplicationRuntime.mutations != null ? ApplicationRuntime.mutations : notRegistered(IFoundgineMutations.class),
                security, hub);
        var approvals = new FoundgineGatewayApprovalService(execution, new InMemoryFoundgineGatewayApprovalStore(), security);
        IFoundgineGatewaySemanticSource semantic = ApplicationRuntime.foundgine != null
                ? new FoundgineGatewaySemanticSource(ApplicationRuntime.foundgine)
                : () -> { throw new IllegalStateException("Register the application's IFoundgine in ApplicationRuntime."); };

        var services = FoundgineGatewayHttpServices.builder().gateway(gateway).semanticSource(semantic)
                .execution(execution).approvals(approvals).hub(hub).security(security).authorizer(authorizer).build();

        // ---- routes: the gateway plus /health and /providers (both anonymous, as in the C# sample) ---------------
        var routes = new ArrayList<>(FoundgineGatewayRoutes.map(services).routes());
        routes.add(new GatewayHttpRoute("GET", "/health", false, (req, res) -> res.writeJson("{\"status\":\"ok\"}")));
        var mapper = new ObjectMapper();
        routes.add(new GatewayHttpRoute("GET", "/providers", false, (req, res) -> {
            var rows = new ArrayList<Map<String, Object>>();
            for (var provider : providers) {
                var row = new LinkedHashMap<String, Object>();
                row.put("id", provider.id());
                row.put("name", provider.name());
                row.put("version", provider.version());
                row.put("kind", provider.kind().toString());
                row.put("capabilities", provider.capabilities());
                rows.add(row);
            }
            res.writeJson(mapper.writeValueAsString(rows));
        }));
        var router = new GatewayHttpRouter(routes, authorizer)
                .onUnhandled(failure -> System.err.println("Unhandled gateway failure: " + failure));

        // ---- host ------------------------------------------------------------------------------------------
        var server = GatewayJdkHttpServer.builder(router).host(host).port(port)
                .afterRequest(requestContext::remove)
                .onError(failure -> System.err.println("Gateway host failure: " + failure)).build();
        var stopped = new CountDownLatch(1);
        Runtime.getRuntime().addShutdownHook(new Thread(() -> { server.stop(2); stopped.countDown(); }));
        server.start();
        System.out.println(gatewayName + " listening on http://" + host + ":" + server.port()
                + (postgresUrl == null ? " (in-memory event journal)" : " (PostgreSQL event journal)"));
        if (!authorizer.configured())
            System.out.println("FOUNDGINE_SAMPLE_DEV_TOKEN is not set: protected routes answer 401. "
                    + "Plug a real authenticator into GatewayHttpAuthorizer for anything beyond a local demo.");
        stopped.await();
    }

    /** Placeholder for an application-owned contract: fails with a clear message when first used. */
    @SuppressWarnings("unchecked")
    private static <T> T notRegistered(Class<T> contract) {
        return (T) java.lang.reflect.Proxy.newProxyInstance(contract.getClassLoader(), new Class<?>[] {contract},
                (proxy, method, args) -> {
                    throw new IllegalStateException(contract.getSimpleName()
                            + " is application-owned; register it in ApplicationRuntime.");
                });
    }

    /** {@code ConnectionStrings:Postgres} equivalent: accepts a jdbc: URL or the Npgsql key=value form. */
    static String resolvePostgresUrl(String value) {
        if (value == null || value.isBlank()) return null;
        if (value.startsWith("jdbc:")) return value;
        var parts = new LinkedHashMap<String, String>();
        for (var item : value.split(";")) {
            int eq = item.indexOf('=');
            if (eq > 0) parts.put(item.substring(0, eq).trim().toLowerCase(java.util.Locale.ROOT), item.substring(eq + 1).trim());
        }
        return "jdbc:postgresql://" + parts.getOrDefault("host", "localhost") + ":" + parts.getOrDefault("port", "5432")
                + "/" + parts.getOrDefault("database", "postgres")
                + "?user=" + parts.getOrDefault("username", parts.getOrDefault("user", "postgres"))
                + "&password=" + parts.getOrDefault("password", "");
    }

    /**
     * DEV ONLY. Accepts one static bearer token and maps it to a fixed subject/tenant. A production host validates a
     * real credential (for example an OIDC access token) and the Foundgine warrant, and only then establishes the
     * {@link SecurityExecutionContext}. Never derive that context from request JSON. Fails closed: with no token
     * configured, every protected route is unauthenticated.
     */
    static final class DevTokenAuthorizer implements GatewayHttpAuthorizer {
        private final byte[] token;
        private final String subject;
        private final String tenant;
        private final ThreadLocal<SecurityExecutionContext> holder;

        DevTokenAuthorizer(String token, String subject, String tenant, ThreadLocal<SecurityExecutionContext> holder) {
            this.token = token == null || token.isBlank() ? null : token.getBytes(StandardCharsets.UTF_8);
            this.subject = subject;
            this.tenant = tenant;
            this.holder = holder;
        }

        boolean configured() { return token != null; }

        @Override public Decision authorize(GatewayHttpRequest request) {
            if (token == null) return Decision.UNAUTHENTICATED;
            var header = request.header("Authorization");
            if (header == null || !header.regionMatches(true, 0, "Bearer ", 0, 7)) return Decision.UNAUTHENTICATED;
            var presented = header.substring(7).trim().getBytes(StandardCharsets.UTF_8);
            if (!MessageDigest.isEqual(presented, token)) return Decision.UNAUTHENTICATED;
            var warrant = SecurityWarrant.ofDefaults("dev-warrant", "gateway-minimal-api-sample", subject, "foundgine-api",
                    List.of(), SecurityWarrantConstraints.UNRESTRICTED, Instant.now(), Instant.now().plusSeconds(300),
                    "dev-nonce", "dev-key", null, null);
            holder.set(new SecurityExecutionContext(warrant, subject, "foundgine-api", tenant, null));
            return Decision.ALLOWED;
        }
    }
}
