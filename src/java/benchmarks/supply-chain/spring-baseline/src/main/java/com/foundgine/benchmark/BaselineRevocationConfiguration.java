package com.foundgine.benchmark;

import io.lettuce.core.RedisClient;
import io.lettuce.core.api.StatefulRedisConnection;
import io.lettuce.core.api.sync.RedisCommands;
import javax.sql.DataSource;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;

@Configuration public class BaselineRevocationConfiguration {
 @Bean(destroyMethod="shutdown") RedisClient benchmarkRedisClient(@Value("${foundgine.benchmark.redis-uri:redis://localhost:6379}") String uri) { return RedisClient.create(uri); }
 @Bean(destroyMethod="close") StatefulRedisConnection<String,String> benchmarkRedisConnection(RedisClient c) { return c.connect(); }
 @Bean RedisCommands<String,String> benchmarkRedisCommands(StatefulRedisConnection<String,String> c) { return c.sync(); }
 @Bean BaselineRevocationEpochGuard baselineRevocationEpochGuard(RedisCommands<String,String> redis, DataSource ds) {
  var guard = new BaselineRevocationEpochGuard(redis, ds); guard.migrate(); return guard;
 }
}
