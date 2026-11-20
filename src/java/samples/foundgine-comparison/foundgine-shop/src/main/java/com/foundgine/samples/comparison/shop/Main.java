package com.foundgine.samples.comparison.shop;

import com.foundgine.core.semantic.metadata.SemanticModelDiscovery;
import com.foundgine.core.semantic.security.warrants.MemorySecurityWarrantReplayStore;
import com.foundgine.extensions.graphql.FoundgineGraphQLQueryExecutor;
import com.foundgine.extensions.graphql.GraphQLSemanticAdapter;
import com.foundgine.extensions.graphql.http.GraphQLHttpHandler;
import com.foundgine.extensions.keycloak.configuration.FoundgineKeycloakOptions;
import com.foundgine.extensions.keycloak.warrants.FoundgineKeycloakWarrantIssuer;
import com.foundgine.extensions.keycloak.warrants.FoundgineWarrantKeyResolver;
import com.foundgine.extensions.keycloak.warrants.PemWarrantSigningKeyProvider;
import com.foundgine.providers.storage.orm.jpa.FoundgineOrm;
import com.foundgine.providers.storage.orm.jpa.JpaOrmProvider;
import com.foundgine.runtime.FoundgineOptions;
import com.foundgine.runtime.FoundgineServiceCollectionExtensions;
import com.foundgine.runtime.FoundgineServiceRegistry;
import com.foundgine.runtime.IFoundgineExecutor;
import com.foundgine.samples.comparison.shop.generated.GeneratedJpaModels;
import com.foundgine.samples.comparison.shop.generated.GeneratedOrmMaps;
import com.sun.net.httpserver.HttpServer;
import jakarta.persistence.Persistence;
import java.net.InetSocketAddress;
import java.util.Map;
import java.util.concurrent.Executors;

/** Foundgine side of the comparison: same API as spring-shop, on :5001. */
public final class Main {
    public static void main(String[] args) throws Exception {
        String db = env("SHOP_DB_URL", "jdbc:postgresql://localhost:5433/shop");
        String signingKey = env("JWT_SIGNING_KEY", "dev-only-signing-key-change-me-32bytes!!");

        // ---- Foundgine: model, JPA provider, warrant bridge, engine ----
        var metadata = new ShopModel().build();
        var semanticModel = SemanticModelDiscovery.fromMetadata(metadata).build().freeze();
        var emf = Persistence.createEntityManagerFactory("foundgine-shop", Map.of(
                "jakarta.persistence.jdbc.url", db, "jakarta.persistence.jdbc.user", "shop", "jakarta.persistence.jdbc.password", "shop"));
        // FoundgineOrm validates the entity <-> attribute mapping and builds the registry. The engine below owns
        // authorization, so the facade's own (non-engine) read path is closed off.
        var orm = FoundgineOrm.<Void>configure()
                .metadata(metadata)
                .entities(GeneratedOrmMaps.entityTypes())
                .generatedModels(GeneratedJpaModels.create())
                .attributeNaming(FoundgineOrm.AttributeNaming.CAMEL_CASE)
                .authorization(new FoundgineOrm.Authorization<>() {
                    @Override public com.foundgine.core.semantic.authorization.ISemanticAuthorizationPolicy policy(Void caller, com.foundgine.core.semantic.metadata.MetadataRegistry m) {
                        throw new UnsupportedOperationException("Reads go through the Foundgine engine.");
                    }
                    @Override public com.foundgine.core.execution.ExecutionContext executionContext(Void caller) {
                        throw new UnsupportedOperationException("Reads go through the Foundgine engine.");
                    }
                })
                .build();

        var keycloak = new FoundgineKeycloakOptions();
        keycloak.warrantIssuer = "shop-jwt-bridge";
        keycloak.warrantAudience = "foundgine";
        keycloak.warrantKeyId = "shop-warrant-1";
        ShopAuth.mapGrants(keycloak);
        var keys = PemWarrantSigningKeyProvider.createEphemeral(keycloak.warrantKeyId); // single instance only; use a KMS/mounted key to scale out
        var issuer = new FoundgineKeycloakWarrantIssuer(keycloak, keys);

        var options = new FoundgineOptions()
                .model(semanticModel)
                .authorizationPolicy(new ShopPolicy(metadata))
                .warrantKeyResolver(FoundgineWarrantKeyResolver.create(keycloak, keys))
                .expectedWarrantIssuer(keycloak.warrantIssuer)
                .warrantReplayStore(new MemorySecurityWarrantReplayStore());
        var services = FoundgineServiceCollectionExtensions.addFoundgine(
                new FoundgineServiceRegistry(), options, JpaOrmProvider.compiler(orm.registry()), JpaOrmProvider.execution(orm.registry(), emf));

        // ---- Protocol 1: GraphQL ----
        var queries = new FoundgineGraphQLQueryExecutor(
                services.getRequiredService(IFoundgineExecutor.class), new GraphQLSemanticAdapter(semanticModel),
                new ShopAuth.JwtSecurityContextProvider(issuer), ShopPolicy::context);
        var graphql = new GraphQLHttpHandler.Options();
        ShopGraphQL.configure(graphql);

        var server = HttpServer.create(new InetSocketAddress(5001), 0);
        new GraphQLHttpHandler(queries, null, semanticModel, new ShopAuth.Jwt(signingKey), graphql).register(server);
        server.setExecutor(Executors.newFixedThreadPool(16)); // the default executor serves one request at a time
        server.start();
    }

    private static String env(String name, String fallback) {
        var v = System.getenv(name);
        return v == null || v.isBlank() ? fallback : v;
    }
}
