package com.foundgine.spring;

import com.foundgine.providers.storage.orm.jpa.FoundgineOrm;

import jakarta.persistence.EntityManagerFactory;

import org.springframework.beans.factory.ObjectProvider;
import org.springframework.boot.autoconfigure.AutoConfiguration;
import org.springframework.boot.autoconfigure.condition.ConditionalOnBean;
import org.springframework.boot.autoconfigure.condition.ConditionalOnClass;
import org.springframework.boot.autoconfigure.condition.ConditionalOnMissingBean;
import org.springframework.context.annotation.Bean;

/**
 * Registers a {@link FoundgineOrm} from the application's {@link FoundgineOrm.Authorization} bean and any
 * {@link FoundgineOrmCustomizer} beans. Does nothing unless an Authorization bean exists, so authorization is
 * never defaulted. The caller type is erased at the bean boundary; applications with a single caller type can
 * inject {@code FoundgineOrm<MyCaller>}.
 *
 * <p>Condition and ordering annotations are deliberately minimal because they were written without the Spring
 * Boot jars. UNVERIFIED.
 */
@AutoConfiguration
@ConditionalOnClass({FoundgineOrm.class, EntityManagerFactory.class})
public class FoundgineJpaAutoConfiguration {
    @Bean
    @ConditionalOnBean(FoundgineOrm.Authorization.class)
    @ConditionalOnMissingBean(FoundgineOrm.class)
    @SuppressWarnings({"unchecked", "rawtypes"})
    public FoundgineOrm<?> foundgineOrm(FoundgineOrm.Authorization authorization,
            ObjectProvider<FoundgineOrmCustomizer> customizers) {
        FoundgineOrm.Builder builder = FoundgineOrm.configure();
        customizers.orderedStream().forEach(c -> c.customize(builder));
        return builder.authorization(authorization).build();
    }
}
