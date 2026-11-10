package com.foundgine.providers.storage.orm;

import com.foundgine.core.execution.ExecutionIR;
import com.foundgine.core.execution.ProviderPlan;

import java.util.Objects;

/** Provider plan of the ORM providers: the validated ExecutionIR itself. */
public final class OrmPlan extends ProviderPlan {
    private final ExecutionIR ir;

    public OrmPlan(String provider, ExecutionIR ir) {
        super(provider);
        this.ir = Objects.requireNonNull(ir, "ir");
    }

    public ExecutionIR ir() {
        return ir;
    }
}
