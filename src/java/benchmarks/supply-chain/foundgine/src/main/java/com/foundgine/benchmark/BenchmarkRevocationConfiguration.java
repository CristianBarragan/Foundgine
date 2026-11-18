package com.foundgine.benchmark;

import com.foundgine.core.semantic.security.warrants.ISecurityWarrantRevocationEpochAnchor;
import com.foundgine.core.semantic.security.warrants.ISecurityWarrantRevocationStore;
//import com.foundgine.providers.security.postgres.PostgresSecurityWarrantRevocationEpochAnchor;
//import com.foundgine.providers.security.postgres.PostgresSecurityWarrantRevocationEpochAnchorMigrator;
import com.foundgine.providers.security.postgres.*;
import com.foundgine.providers.security.redis.*;
//import com.foundgine.providers.security.redis.AnchoredSecurityWarrantRevocationStore;
//import com.foundgine.providers.security.redis.LettuceRevocationBackend;
//import com.foundgine.providers.security.redis.RedisSecurityWarrantRevocationStore;
import io.lettuce.core.RedisClient;
import io.lettuce.core.api.StatefulRedisConnection;
import io.lettuce.core.api.sync.RedisCommands;
import javax.sql.DataSource;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;
import org.springframework.context.annotation.DependsOn;
import org.springframework.context.annotation.Primary;

@Configuration
public class BenchmarkRevocationConfiguration {
    @Bean(destroyMethod = "shutdown")
    RedisClient benchmarkRedisClient(@Value("${foundgine.benchmark.redis-uri:redis://localhost:6379}") String uri) {
        return RedisClient.create(uri);
    }
    @Bean(destroyMethod = "close")
    StatefulRedisConnection<String, String> benchmarkRedisConnection(RedisClient client) { return client.connect(); }
    @Bean
    RedisCommands<String, String> benchmarkRedisCommands(StatefulRedisConnection<String, String> connection) { return connection.sync(); }
    @Bean
    RedisSecurityWarrantRevocationStore redisWarrantRevocationStore(RedisCommands<String, String> commands) {
        return new RedisSecurityWarrantRevocationStore(new LettuceRevocationBackend(commands), "benchmark:java:");
    }
    // SupplyChainGraphQl creates SupplyChainJpa, which runs the Foundgine-generated Flyway migration. The anchor table must be
    // created AFTER it: Flyway refuses a non-empty schema that has no schema history table.
    @Bean
    @DependsOn("supplyChainGraphQl")
    ISecurityWarrantRevocationEpochAnchor warrantRevocationEpochAnchor(DataSource dataSource) {
        PostgresSecurityWarrantRevocationEpochAnchorMigrator.apply(dataSource);
        return new PostgresSecurityWarrantRevocationEpochAnchor(dataSource);
    }
    // RedisSecurityWarrantRevocationStore also implements ISecurityWarrantRevocationStore; consumers of the
    // interface must receive the anchored (durable high-water) store, never the bare Redis one.
    @Bean
    @Primary
    ISecurityWarrantRevocationStore warrantRevocationStore(RedisSecurityWarrantRevocationStore redis,
            ISecurityWarrantRevocationEpochAnchor anchor) {
        return new AnchoredSecurityWarrantRevocationStore(redis, anchor);
    }
}
