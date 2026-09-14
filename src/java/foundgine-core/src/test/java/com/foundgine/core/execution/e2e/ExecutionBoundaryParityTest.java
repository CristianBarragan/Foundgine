package com.foundgine.core.execution.e2e;

import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.execution.ExecutionCellKey;
import com.foundgine.core.execution.ExecutionResult;
import com.foundgine.core.execution.ExecutionRow;
import com.foundgine.core.execution.ResultMaterializer;
import com.foundgine.core.semantic.SemanticModelBuilder;
import com.foundgine.core.semantic.planning.ExecutionOperation;
import com.foundgine.core.semantic.planning.SemanticPlan;
import com.foundgine.core.semantic.planning.SemanticPlanNode;
import org.junit.jupiter.api.Test;

import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

import static org.junit.jupiter.api.Assertions.assertEquals;

/**
 * Port of {@code Foundgine.E2E.Tests.ExecutionBoundaryTests}.
 *
 * <p>
 * <b>Porting decisions:</b>
 * <ul>
 * <li>{@code Execution_assembly_does_not_reference_metadata_directly} checks,
 * via {@code Assembly.GetReferencedAssemblies}, that the assembly containing
 * {@code ExecutionResult} does not reference an assembly named
 * {@code Foundgine.Core.Semantic.Metadata}. That name is a C# namespace inside
 * the very same {@code Foundgine.Core} assembly as {@code ExecutionResult}
 * (there is no separate {@code Foundgine.Core.Semantic.Metadata} project), so
 * {@code GetReferencedAssemblies} — which only ever lists external assemblies
 * — can never contain it; the assertion is vacuously true regardless of any
 * real dependency and encodes no portable behavior. Java's module layout
 * mirrors the C# one (metadata support lives in the same
 * {@code foundgine-core} module as execution), so the equivalent check would
 * be equally vacuous here and is not ported.</li>
 * <li>{@code Result_materialization_uses_semantic_topology_not_storage_metadata}
 * is a real functional case: a scan-only (no relationships) plan node's
 * materialized root must expose every scanned field — identity and
 * non-identity alike — through {@code values()}, not just the identity used
 * to key rows. The existing
 * {@code ResultMaterializationParityE2ETest} in this package exercises
 * {@code ResultMaterializer} with relationship traversal and null-identity
 * handling, and asserts a child node's {@code values()}, but never asserts a
 * root node's own {@code values()} map — every existing assertion on a root
 * goes through {@code identityValue()} instead. This test closes that gap.</li>
 * </ul>
 */
class ExecutionBoundaryParityTest {

	@Test
	void resultMaterializationExposesAllScannedFieldsOnTheRootNotJustIdentity() {
		var customer = EntityId.create("Customer");
		var id = FieldId.create("Customer", "Id");
		var name = FieldId.create("Customer", "Name");

		var model = new SemanticModelBuilder()
				.entity(customer, "Customer", e -> e.identity(id, "Id").field(id, "Id", Long.class)
						.field(name, "Name", String.class))
				.build();

		var plan = new SemanticPlan(
				new SemanticPlanNode(1, ExecutionOperation.SCAN, customer, List.of(id, name), null, null, List.of()));

		Map<ExecutionCellKey, Object> cells = new LinkedHashMap<>();
		cells.put(new ExecutionCellKey(1, customer, id), 42L);
		cells.put(new ExecutionCellKey(1, customer, name), "Ada");
		var row = new ExecutionRow(Map.of(), cells);

		var materialized = new ResultMaterializer(model).materialize(plan, new ExecutionResult(List.of(row)));

		assertEquals(1, materialized.roots().size());
		var root = materialized.roots().get(0);
		assertEquals(42L, root.values().get(id));
		assertEquals("Ada", root.values().get(name));
	}
}
