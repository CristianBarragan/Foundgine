package com.foundgine.core.execution.mutation;

import org.junit.jupiter.api.Test;

import java.lang.reflect.Field;
import java.lang.reflect.Member;
import java.lang.reflect.Method;
import java.lang.reflect.Modifier;
import java.lang.reflect.ParameterizedType;
import java.util.Arrays;
import java.util.List;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertTrue;

/**
 * Port of {@code Foundgine.E2E.Tests.ProviderBoundaryTests}.
 *
 * <p>
 * <b>Porting decisions:</b>
 * <ul>
 * <li>{@code Provider_mutation_plan_is_opaque_to_planning_types} and
 * {@code Provider_mutation_batch_plan_exposes_only_provider_plans} check, via
 * reflection, that the two opaque provider-mutation-plan marker types don't
 * leak provider-specific shape to the execution layer. C#'s
 * {@code GetProperties}/{@code GetFields} become Java's
 * {@code getDeclaredMethods}/{@code getDeclaredFields} filtered to
 * {@code public}; Java has no property concept distinct from methods, so the
 * "no public surface beyond the documented one" check is expressed as "no
 * public methods/fields other than the ones the class is documented to
 * have" rather than an exact API match to the C# reflection calls.</li>
 * <li>{@code Execution_does_not_reference_sql_provider} checks, via assembly
 * references, that the execution layer's assembly doesn't reference the SQL
 * provider assembly. Maven has no equivalent of a compiled assembly
 * reference list to inspect at runtime; the module-level version of this
 * boundary (the {@code foundgine-core} POM declares no dependency on any
 * provider/transport module, including {@code sql}) is already covered by
 * {@code ArchitectureBoundaryParityTest#coreModulesDoNotReferenceTransportOrProviderPackages}
 * in {@code com.foundgine.core.semantic.planning}, so it is not duplicated
 * here.</li>
 * </ul>
 */
class ProviderBoundaryParityTest {

	@Test
	void providerMutationPlanIsOpaqueToPlanningTypes() {
		Class<?> type = ProviderMutationPlan.class;

		assertTrue(publicInstanceFields(type).isEmpty(),
				"ProviderMutationPlan must expose no public instance fields: " + publicInstanceFields(type));
		assertTrue(publicInstanceMethods(type).isEmpty(),
				"ProviderMutationPlan must expose no public instance methods: " + publicInstanceMethods(type));
	}

	@Test
	void providerMutationBatchPlanExposesOnlyProviderPlans() throws NoSuchMethodException {
		Method operations = ProviderMutationBatchPlan.class.getMethod("operations");

		assertEquals(List.class, operations.getReturnType());

		ParameterizedType genericReturnType = (ParameterizedType) operations.getGenericReturnType();
		assertEquals(ProviderMutationPlan.class, genericReturnType.getActualTypeArguments()[0]);

		// The only public instance member should be operations() itself.
		assertEquals(List.of("operations"), publicInstanceMethods(ProviderMutationBatchPlan.class));
	}

	private static List<String> publicInstanceFields(Class<?> type) {
		return Arrays.stream(type.getDeclaredFields()).filter(ProviderBoundaryParityTest::isPublicInstance)
				.map(Field::getName).toList();
	}

	private static List<String> publicInstanceMethods(Class<?> type) {
		return Arrays.stream(type.getDeclaredMethods()).filter(ProviderBoundaryParityTest::isPublicInstance)
				.map(Method::getName).toList();
	}

	private static boolean isPublicInstance(Member member) {
		int modifiers = member.getModifiers();
		return Modifier.isPublic(modifiers) && !Modifier.isStatic(modifiers) && !member.isSynthetic();
	}
}
