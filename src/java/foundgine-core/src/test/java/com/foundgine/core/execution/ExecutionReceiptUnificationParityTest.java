package com.foundgine.core.execution;

import org.junit.jupiter.api.Test;

import java.time.Instant;
import java.util.List;

import static org.junit.jupiter.api.Assertions.*;

/**
 * Port of C# {@code Foundgine.Providers.Tools.MCP.Tests.ExecutionReceiptUnificationTests}.
 *
 * <p>
 * <b>Porting decisions:</b>
 * <ul>
 * <li>{@code ExecutionReceiptFactory} and {@code ExecutionReceipt} already
 * exist in {@code foundgine-core} with no test coverage at all - this closes
 * that gap rather than duplicating an existing port.</li>
 * <li>The Java factory has no overload that omits the approval parameters
 * (unlike the C# original's optional trailing parameters), so the read-path
 * case below passes {@code null} explicitly for {@code approvalId},
 * {@code approvedBy}, and {@code approvedAt} instead of relying on a
 * shorter overload.</li>
 * <li>The C# original asserts {@code AffectedNodeIds} preserves the input
 * order {@code [8123, 55]} verbatim; the Java factory always
 * distinct-and-sorts {@code affectedNodeIds} (see
 * {@code ExecutionReceiptFactory.distinctSorted}), so the equivalent
 * assertion here expects the sorted order {@code [55, 8123]} - this is a
 * genuine, deliberate behavioral difference from the C# port, not a mistake
 * in this test.</li>
 * </ul>
 */
class ExecutionReceiptUnificationParityTest {

	@Test
	void canonicalReceiptCanRepresentReadExecution() {
		var evidence = new ExecutionEvidence("sql", "plan-fp", List.of(1, 2), 10, 4, null, "intent-fp", "auth-fp",
				null, null, null, null);

		Instant started = Instant.now();
		ExecutionReceipt receipt = ExecutionReceiptFactory.create("req-read", evidence, "result-fp", List.of(1, 2),
				List.of("read"), started, started.plusMillis(4), 1, 1, 1, 1, "model-1", null, null, null);

		assertEquals("succeeded", receipt.status());
		assertNull(receipt.approvalId());
		assertEquals("plan-fp", receipt.planFingerprint());
		assertEquals("result-fp", receipt.resultFingerprint());
	}

	@Test
	void canonicalReceiptCanRepresentApprovedMutationExecution() {
		var evidence = new ExecutionEvidence("postgres", "mutation-plan-fp", List.of(8123, 55), 1, 12, null,
				"mutation-intent-fp", "mutation-auth-fp", null, null, null, null);

		Instant started = Instant.now();
		Instant approved = started.minusSeconds(1);

		ExecutionReceipt receipt = ExecutionReceiptFactory.create("req-write", evidence, "result-fp",
				List.of(8123, 55), List.of("Order.update", "Payment.refund", "Audit.create"), started,
				started.plusMillis(12), 1, 2, 1, 1, "model-2", "approval-1", "operator", approved);

		assertEquals("succeeded", receipt.status());
		assertEquals("approval-1", receipt.approvalId());
		assertEquals("operator", receipt.approvedBy());
		assertTrue(receipt.effects().contains("Payment.refund"));
		// Distinct-sorted, unlike the C# port which preserves input order - see class javadoc.
		assertEquals(List.of(55, 8123), receipt.affectedNodeIds());
	}

	@Test
	void createRejectsEvidenceMissingRequiredFingerprints() {
		var missingIntentFingerprint = new ExecutionEvidence("sql", "plan-fp", List.of(1), 1, 1, null, null,
				"auth-fp", null, null, null, null);
		Instant now = Instant.now();

		assertThrows(IllegalStateException.class,
				() -> ExecutionReceiptFactory.create("req", missingIntentFingerprint, "result-fp", List.of(1),
						List.of("read"), now, now, 1, 1, 1, 1, "model"));
	}
}
