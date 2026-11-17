package com.foundgine.benchmark;
import org.springframework.context.annotation.Bean; import org.springframework.context.annotation.Configuration;
import org.springframework.security.config.Customizer; import org.springframework.security.config.annotation.web.builders.HttpSecurity;
import org.springframework.security.oauth2.server.resource.web.authentication.BearerTokenAuthenticationFilter;
import org.springframework.security.config.http.SessionCreationPolicy;
import org.springframework.security.web.SecurityFilterChain;
@Configuration public class SecurityConfiguration {
 @Bean SecurityFilterChain security(HttpSecurity http, BaselineRevocationEpochGuard guard) throws Exception {
	 return http
			    .sessionManagement(s -> s.sessionCreationPolicy(SessionCreationPolicy.STATELESS))
			    .csrf(csrf -> csrf.ignoringRequestMatchers(request -> {
			        String auth = request.getHeader("Authorization");
			        return auth != null && auth.startsWith("Bearer ");
			    }))
			    .authorizeHttpRequests(a -> a.requestMatchers("/health/ready").permitAll().anyRequest().authenticated())
			    .oauth2ResourceServer(o -> o.jwt(Customizer.withDefaults()))
			    .addFilterAfter(new BaselineRevocationFilter(guard), BearerTokenAuthenticationFilter.class)
			    .build();
 }
}
