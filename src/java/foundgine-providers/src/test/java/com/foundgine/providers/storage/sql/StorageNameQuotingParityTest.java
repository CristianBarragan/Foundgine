package com.foundgine.providers.storage.sql;

import com.foundgine.core.abstractions.ColumnId;
import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.semantic.SemanticModelBuilder;
import com.foundgine.core.semantic.SemanticRequest;
import com.foundgine.core.semantic.SemanticSelection;
import com.foundgine.core.semantic.authorization.AllowAllSemanticAuthorizationPolicy;
import com.foundgine.core.semantic.authorization.SemanticAuthorizer;
import com.foundgine.core.semantic.ir.SemanticOperationCompiler;
import com.foundgine.core.semantic.metadata.ColumnMetadata;
import com.foundgine.core.semantic.metadata.ColumnReference;
import com.foundgine.core.semantic.metadata.EntityMetadata;
import com.foundgine.core.semantic.metadata.FieldMetadata;
import com.foundgine.core.semantic.metadata.MetadataRegistry;
import com.foundgine.core.semantic.planning.Planner;
import com.foundgine.core.semantic.planning.SemanticPlan;
import com.foundgine.core.semantic.planning.SemanticPlanAuthorizationBinding;
import com.foundgine.core.semantic.resolution.SemanticRequestResolver;
import org.junit.jupiter.api.Test;

import java.util.List;

import static org.junit.jupiter.api.Assertions.*;

/**
 * Port of C# {@code Foundgine.E2E.Tests.StorageNameQuotingTests}:
 * schema-qualified storage names ("Banking.Customer") must be rendered as two
 * separately quoted identifiers ({@code "Banking"."Customer"}), not one
 * identifier containing a literal dot.
 */
class StorageNameQuotingParityTest {

	private static final EntityId CUSTOMER = EntityId.create("Customer");
	private static final FieldId CUSTOMER_ID = FieldId.create("Customer", "Id");
	private static final FieldId CUSTOMER_NAME = FieldId.create("Customer", "Name");

	@Test
	void schemaQualifiedStorageNamesAreQuotedPerIdentifier() {
		var model = new SemanticModelBuilder().entity(CUSTOMER, "Customer", e -> e.identity(CUSTOMER_ID, "Id")
				.field(CUSTOMER_ID, "Id", Long.class).field(CUSTOMER_NAME, "Name", String.class)).build();

		var metadata = new MetadataRegistry();
		var customerIdColumn = new ColumnId(1);
		var customerNameColumn = new ColumnId(2);
		metadata.register(new EntityMetadata(CUSTOMER, "Customer",
				List.of(new ColumnMetadata(customerIdColumn, "Id"), new ColumnMetadata(customerNameColumn, "Name")),
				"Banking.Customer",
				List.of(new FieldMetadata(CUSTOMER_ID, "Id", Long.class,
						new ColumnReference(CUSTOMER, customerIdColumn)),
						new FieldMetadata(CUSTOMER_NAME, "Name", String.class,
								new ColumnReference(CUSTOMER, customerNameColumn))),
				new ColumnReference(CUSTOMER, customerIdColumn), null, false, null, null));

		var request = new SemanticRequest(CUSTOMER, List.of(new SemanticSelection(CUSTOMER_ID, null, List.of())), null,
				null);

		var contract = new com.foundgine.core.semantic.SemanticContractSnapshot(model.freeze());
		var graph = new SemanticRequestResolver(contract).resolve(request);
		var authorized = new SemanticAuthorizer(new AllowAllSemanticAuthorizationPolicy())
				.authorize(SemanticOperationCompiler.compile(graph));
		var planned = new Planner().plan(authorized);
		var plan = new SemanticPlan(planned.root(), planned.requiredSecurityInvariants(),
				new SemanticPlanAuthorizationBinding("test-contract", "test-authorization"));

		var sql = new SqlCompiler(metadata).compile(plan).commandText();

		assertTrue(sql.contains("FROM \"Banking\".\"Customer\""), sql);
		assertFalse(sql.contains("FROM \"Banking.Customer\""), sql);
	}
}
