package com.foundgine.core.semantic.planning;

import com.foundgine.core.abstractions.*;
import com.foundgine.core.semantic.SemanticGraph;
import com.foundgine.core.semantic.query.SemanticQueryOptions;
import org.junit.jupiter.api.Test;
import java.lang.reflect.*;
import java.util.*;
import static org.junit.jupiter.api.Assertions.*;

class PlanningDependencyBoundaryParityTest {
    @Test void plannerConsumesSemanticGraphWithoutPhysicalMetadata() {
        var graph = new SemanticGraph();
        graph.setOptions(new SemanticQueryOptions(null, List.of(), 5, null, null));
        graph.addRoot(new EntityId(1), List.of(new FieldId(1)));
        var plan = new Planner().plan(graph);
        assertEquals(new EntityId(1), plan.root().entityId());
        assertNotNull(plan.root().queryOptions());
        assertEquals(5, plan.root().queryOptions().limit());
    }

    @Test void semanticPlanPublicContractContainsNoMetadataTypes() {
        for (var type : List.of(SemanticPlan.class, SemanticPlanNode.class)) {
            for (var ctor : type.getDeclaredConstructors())
                for (var p : ctor.getParameterTypes()) assertFalse(containsMetadata(p));
            for (var field : type.getDeclaredFields())
                if (!Modifier.isStatic(field.getModifiers())) assertFalse(containsMetadata(field.getType()));
        }
    }

    private static boolean containsMetadata(Class<?> type) {
        if (type.getName().contains(".metadata.")) return true;
        if (type.isArray()) return containsMetadata(type.getComponentType());
        if (type.isRecord()) for (var c : type.getRecordComponents()) if (containsMetadata(c.getType())) return true;
        if (type.isGenericArray()) return containsMetadata(type.getComponentType());
        for (var arg : type.getTypeParameters()) if (arg.getName().contains("Metadata")) return true;
        return false;
    }
}
