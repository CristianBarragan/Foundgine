package com.foundgine.core.semantic.planning;

import com.foundgine.core.abstractions.*;
import com.foundgine.core.semantic.SemanticGraph;
import com.foundgine.core.semantic.query.SemanticQueryOptions;
import org.junit.jupiter.api.Test;

import java.lang.reflect.*;
import java.util.*;

import static org.junit.jupiter.api.Assertions.*;

class PlanningDependencyBoundaryParityTest {

	@Test
	void plannerConsumesSemanticGraphWithoutPhysicalMetadata() {
		var graph = new SemanticGraph();
		graph.setOptions(new SemanticQueryOptions(null, List.of(), 5, null, null));
		graph.addRoot(new EntityId(1), List.of(new FieldId(1)));

		var plan = new Planner().plan(graph);

		assertEquals(new EntityId(1), plan.root().entityId());
		assertNotNull(plan.root().queryOptions());
		assertEquals(5, plan.root().queryOptions().limit());
	}

	@Test
	void semanticPlanPublicContractContainsNoMetadataTypes() {
		for (var type : List.of(SemanticPlan.class, SemanticPlanNode.class)) {
			Set<Class<?>> visited = Collections.newSetFromMap(new IdentityHashMap<Class<?>, Boolean>());

			for (var ctor : type.getDeclaredConstructors()) {
				for (var parameter : ctor.getGenericParameterTypes()) {
					assertFalse(containsMetadata(parameter, visited),
							() -> type.getName() + " constructor contains metadata type: " + parameter);
				}
			}

			for (var field : type.getDeclaredFields()) {
				if (Modifier.isStatic(field.getModifiers())) {
					continue;
				}

				assertFalse(containsMetadata(field.getGenericType(), visited),
						() -> type.getName() + " field contains metadata type: " + field);
			}
		}
	}

	private static boolean containsMetadata(Type type, Set<Class<?>> visited) {
		if (type == null) {
			return false;
		}

		if (type instanceof Class<?> clazz) {
			if (clazz.getName().contains(".metadata.")) {
				return true;
			}

			if (!visited.add(clazz)) {
				return false;
			}

			if (clazz.isArray()) {
				return containsMetadata(clazz.getComponentType(), visited);
			}

			if (clazz.isRecord()) {
				for (var component : clazz.getRecordComponents()) {
					if (containsMetadata(component.getGenericType(), visited)) {
						return true;
					}
				}
			}

			return false;
		}

		if (type instanceof ParameterizedType parameterizedType) {
			if (containsMetadata(parameterizedType.getRawType(), visited)) {
				return true;
			}

			for (var argument : parameterizedType.getActualTypeArguments()) {
				if (containsMetadata(argument, visited)) {
					return true;
				}
			}

			return false;
		}

		if (type instanceof GenericArrayType genericArrayType) {
			return containsMetadata(genericArrayType.getGenericComponentType(), visited);
		}

		if (type instanceof WildcardType wildcardType) {
			for (var upper : wildcardType.getUpperBounds()) {
				if (containsMetadata(upper, visited)) {
					return true;
				}
			}

			for (var lower : wildcardType.getLowerBounds()) {
				if (containsMetadata(lower, visited)) {
					return true;
				}
			}

			return false;
		}

		if (type instanceof TypeVariable<?> variable) {
			if (variable.getName().contains("Metadata")) {
				return true;
			}

			for (var bound : variable.getBounds()) {
				if (containsMetadata(bound, visited)) {
					return true;
				}
			}

			return false;
		}

		return false;
	}
}