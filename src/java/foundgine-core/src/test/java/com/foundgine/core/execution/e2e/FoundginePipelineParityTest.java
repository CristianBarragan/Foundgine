package com.foundgine.core.execution.e2e;

import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.abstractions.RelationshipId;
import com.foundgine.core.semantic.RelationshipCardinality;
import com.foundgine.core.semantic.SemanticModel;
import com.foundgine.core.semantic.SemanticModelBuilder;
import com.foundgine.core.semantic.SemanticRequest;
import com.foundgine.core.semantic.SemanticSelection;
import com.foundgine.core.semantic.authorization.AllowAllSemanticAuthorizationPolicy;
import com.foundgine.core.semantic.authorization.SemanticAuthorizer;
import com.foundgine.core.semantic.ir.SemanticOperationCompiler;
import com.foundgine.core.semantic.planning.ExecutionOperation;
import com.foundgine.core.semantic.planning.Planner;
import com.foundgine.core.semantic.resolution.SemanticRequestResolver;
import org.junit.jupiter.api.Test;

import java.util.List;

import static org.junit.jupiter.api.Assertions.assertEquals;

/**
 * Port of {@code Foundgine.E2E.Tests.FoundginePipelineTests}.
 *
 * <p>
 * <b>Porting decisions:</b>
 * <ul>
 * <li>The C# original resolves this against the archived
 * {@code Foundgine.E2E.Tests.Banking.BankingSemanticModel} fixture (Customer
 * -&gt; Account -&gt; Transaction with sequential integer ids like
 * {@code new EntityId(1)}). No Java port of that fixture exists anywhere in
 * this repository (the same gap already noted by
 * {@code AggregateExistenceSqlRenderingParityTest}), and none of the existing
 * Java fixtures (e.g. {@code SqlCompilerParityTest}'s Customer/Account pair)
 * go three entities deep. Rather than invent a shared Banking fixture this
 * test builds its own minimal Customer -&gt; Account -&gt; Transaction model
 * inline, using the name-derived {@code EntityId.create}/{@code FieldId.create}
 * identities the Java port actually uses instead of the C# original's literal
 * sequential ids.</li>
 * <li>This is a genuine gap, not a duplicate: no existing Java test drives
 * the full {@code SemanticRequest -> SemanticRequestResolver ->
 * SemanticAuthorizer -> Planner} pipeline with two levels of relationship
 * traversal and asserts the resulting nested plan tree. The closest existing
 * coverage ({@code ExecutionAlgebraInvariantParityTest}) builds a
 * {@code SemanticGraph} directly, bypassing resolution and authorization
 * entirely, and only goes one level deep.</li>
 * </ul>
 */
class FoundginePipelineParityTest {

	private static final EntityId CUSTOMER = EntityId.create("Customer");
	private static final EntityId ACCOUNT = EntityId.create("Account");
	private static final EntityId TRANSACTION = EntityId.create("Transaction");

	private static final FieldId CUSTOMER_ID = FieldId.create("Customer", "Id");
	private static final FieldId ACCOUNT_ID = FieldId.create("Account", "Id");
	private static final FieldId TRANSACTION_ID = FieldId.create("Transaction", "Id");
	private static final FieldId TRANSACTION_AMOUNT = FieldId.create("Transaction", "Amount");

	private static final RelationshipId CUSTOMER_ACCOUNTS = RelationshipId.create("Customer", "Accounts");
	private static final RelationshipId ACCOUNT_TRANSACTIONS = RelationshipId.create("Account", "Transactions");

	private static SemanticModel model() {
		return new SemanticModelBuilder()
				.entity(CUSTOMER, "Customer",
						e -> e.identity(CUSTOMER_ID, "Id").field(CUSTOMER_ID, "Id", Long.class).relationship(
								CUSTOMER_ACCOUNTS, "Accounts", ACCOUNT, RelationshipCardinality.MANY))
				.entity(ACCOUNT, "Account",
						e -> e.identity(ACCOUNT_ID, "Id").field(ACCOUNT_ID, "Id", Long.class).relationship(
								ACCOUNT_TRANSACTIONS, "Transactions", TRANSACTION, RelationshipCardinality.MANY))
				.entity(TRANSACTION, "Transaction",
						e -> e.identity(TRANSACTION_ID, "Id").field(TRANSACTION_ID, "Id", Long.class)
								.field(TRANSACTION_AMOUNT, "Amount", java.math.BigDecimal.class))
				.build();
	}

	@Test
	void bankingShapedThesisReachesProviderIndependentExecutionPlan() {
		var request = new SemanticRequest(CUSTOMER,
				List.of(new SemanticSelection(CUSTOMER_ID, null, List.of()),
						new SemanticSelection(null, CUSTOMER_ACCOUNTS,
								List.of(new SemanticSelection(ACCOUNT_ID, null, List.of()),
										new SemanticSelection(null, ACCOUNT_TRANSACTIONS,
												List.of(new SemanticSelection(TRANSACTION_ID, null, List.of()),
														new SemanticSelection(TRANSACTION_AMOUNT, null, List.of())))))),
				null, null);

		var model = model();
		var resolved = new SemanticRequestResolver(model.freeze().createSnapshot()).resolve(request);
		var authorized = new SemanticAuthorizer(new AllowAllSemanticAuthorizationPolicy())
				.authorize(SemanticOperationCompiler.compile(resolved));
		var plan = new Planner().plan(authorized);

		assertEquals(CUSTOMER, plan.root().entityId());
		assertEquals(ExecutionOperation.SCAN, plan.root().operation());
		assertEquals(List.of(CUSTOMER_ID), plan.root().fields());

		assertEquals(1, plan.root().children().size());
		var account = plan.root().children().get(0);
		assertEquals(ACCOUNT, account.entityId());
		assertEquals(ExecutionOperation.TRAVERSE, account.operation());
		assertEquals(CUSTOMER_ACCOUNTS, account.viaRelationship());
		assertEquals(List.of(ACCOUNT_ID), account.fields());

		assertEquals(1, account.children().size());
		var transaction = account.children().get(0);
		assertEquals(TRANSACTION, transaction.entityId());
		assertEquals(ExecutionOperation.TRAVERSE, transaction.operation());
		assertEquals(ACCOUNT_TRANSACTIONS, transaction.viaRelationship());
		assertEquals(List.of(TRANSACTION_ID, TRANSACTION_AMOUNT), transaction.fields());
	}
}
