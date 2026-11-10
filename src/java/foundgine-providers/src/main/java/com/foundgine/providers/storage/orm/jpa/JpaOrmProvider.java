package com.foundgine.providers.storage.orm.jpa;

import com.foundgine.providers.storage.orm.OrmExecutionProvider;
import com.foundgine.providers.storage.orm.OrmPlanCompiler;
import com.foundgine.providers.storage.orm.OrmRegistry;

import jakarta.persistence.EntityManager;
import jakarta.persistence.EntityManagerFactory;

/** Convenience wiring of the ORM provider core to JPA. */
public final class JpaOrmProvider {
    public static final String PROVIDER = "orm-jpa";

    private JpaOrmProvider() {}

    public static OrmPlanCompiler compiler(OrmRegistry registry) {
        return new OrmPlanCompiler(PROVIDER, registry);
    }

    public static OrmExecutionProvider execution(OrmRegistry registry, EntityManagerFactory emf) {
        return new OrmExecutionProvider(PROVIDER, new JpaOrmQuerySource(emf), registry);
    }

    public static OrmExecutionProvider execution(OrmRegistry registry, EntityManager em) {
        return new OrmExecutionProvider(PROVIDER, new JpaOrmQuerySource(em), registry);
    }
}
