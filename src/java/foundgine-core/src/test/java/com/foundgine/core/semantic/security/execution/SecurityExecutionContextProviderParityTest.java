package com.foundgine.core.semantic.security.execution;

import com.foundgine.core.semantic.security.warrants.CapabilityGrant;
import com.foundgine.core.semantic.security.warrants.SecurityWarrant;
import com.foundgine.core.semantic.security.warrants.SecurityWarrantConstraints;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.NullSource;
import org.junit.jupiter.params.provider.ValueSource;

import java.time.Instant;
import java.util.List;
import java.util.concurrent.atomic.AtomicInteger;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertNotEquals;
import static org.junit.jupiter.api.Assertions.assertNull;
import static org.junit.jupiter.api.Assertions.assertSame;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

/**
 * Port of the remaining coverage from
 * {@code Foundgine.Core.Semantic.Tests.Security.Execution.SecurityExecutionContextProviderTests}
 * and {@code DelegateSecurityExecutionContextProviderTests} not already
 * exercised by {@link SecurityExecutionContextTest}.
 *
 * <p>
 * <b>Porting decisions:</b>
 * <ul>
 * <li>C#'s {@code ArgumentNullException} for a null provider becomes a
 * {@link NullPointerException} via {@code Objects.requireNonNull}.</li>
 * <li>C#'s {@code ArgumentException} for a blank {@code transportName}/
 * {@code operationDescription} becomes {@link IllegalArgumentException}.</li>
 * </ul>
 */
class SecurityExecutionContextProviderParityTest {

	private static SecurityWarrant warrant() {
		Instant now = Instant.now();
		return SecurityWarrant.ofDefaults("warrant-1", "issuer", "subject-1", "api",
				List.of(new CapabilityGrant("Customer.read", "read", List.of("customer/*"))),
				new SecurityWarrantConstraints(List.of("tenant-a"), null, null, List.of("read"), null, null),
				now.minusSeconds(60), now.plusSeconds(600), "nonce-1", "issuer-key", null, new byte[0]);
	}

	private static SecurityExecutionContext context() {
		return new SecurityExecutionContext(warrant(), "subject-1", "api", "tenant-a", "customer/*");
	}

	@Test
	void requireSecurityExecutionContextReturnsContextWhenPresent() {
		var context = context();
		var provider = new DelegateSecurityExecutionContextProvider(() -> context);

		var result = SecurityExecutionContextProviderExtensions.requireSecurityExecutionContext(provider, "GraphQL",
				"execution");

		assertSame(context, result);
	}

	@Test
	void requireSecurityExecutionContextThrowsWhenMissing() {
		var provider = new DelegateSecurityExecutionContextProvider(() -> null);

		var ex = assertThrows(SecurityException.class, () -> SecurityExecutionContextProviderExtensions
				.requireSecurityExecutionContext(provider, "GraphQL", "execution"));

		assertTrue(ex.getMessage().contains("GraphQL"));
		assertTrue(ex.getMessage().contains("execution"));
		assertTrue(ex.getMessage().contains("SecurityExecutionContext"));
	}

	@Test
	void requireSecurityExecutionContextMessageIdentifiesTransportAndOperationDistinctly() {
		var provider = new DelegateSecurityExecutionContextProvider(() -> null);

		var mcpEx = assertThrows(SecurityException.class, () -> SecurityExecutionContextProviderExtensions
				.requireSecurityExecutionContext(provider, "MCP", "capability discovery"));
		var graphQlEx = assertThrows(SecurityException.class, () -> SecurityExecutionContextProviderExtensions
				.requireSecurityExecutionContext(provider, "GraphQL", "mutation execution"));

		assertNotEquals(mcpEx.getMessage(), graphQlEx.getMessage());
		assertTrue(mcpEx.getMessage().contains("capability discovery"));
		assertTrue(graphQlEx.getMessage().contains("mutation execution"));
	}

	@Test
	void requireSecurityExecutionContextThrowsNullPointerExceptionForNullProvider() {
		ISecurityExecutionContextProvider provider = null;

		assertThrows(NullPointerException.class, () -> SecurityExecutionContextProviderExtensions
				.requireSecurityExecutionContext(provider, "GraphQL", "execution"));
	}

	@ParameterizedTest
	@NullSource
	@ValueSource(strings = { "", "  " })
	void requireSecurityExecutionContextThrowsIllegalArgumentExceptionForBlankTransportName(String transportName) {
		var provider = new DelegateSecurityExecutionContextProvider(SecurityExecutionContextProviderParityTest::context);

		assertThrows(IllegalArgumentException.class, () -> SecurityExecutionContextProviderExtensions
				.requireSecurityExecutionContext(provider, transportName, "execution"));
	}

	@ParameterizedTest
	@NullSource
	@ValueSource(strings = { "", "  " })
	void requireSecurityExecutionContextThrowsIllegalArgumentExceptionForBlankOperationDescription(
			String operationDescription) {
		var provider = new DelegateSecurityExecutionContextProvider(SecurityExecutionContextProviderParityTest::context);

		assertThrows(IllegalArgumentException.class, () -> SecurityExecutionContextProviderExtensions
				.requireSecurityExecutionContext(provider, "GraphQL", operationDescription));
	}

	@Test
	void delegatesToTheSuppliedFactoryOnEachCall() {
		AtomicInteger callCount = new AtomicInteger();
		var context = context();

		var provider = new DelegateSecurityExecutionContextProvider(() -> {
			callCount.incrementAndGet();
			return context;
		});

		var first = provider.getSecurityExecutionContext();
		var second = provider.getSecurityExecutionContext();

		assertSame(context, first);
		assertSame(context, second);
		assertEquals(2, callCount.get());
	}

	@Test
	void passesThroughNullFromTheFactory() {
		var provider = new DelegateSecurityExecutionContextProvider(() -> null);

		assertNull(provider.getSecurityExecutionContext());
	}

	@Test
	void constructorThrowsNullPointerExceptionForNullFactory() {
		assertThrows(NullPointerException.class, () -> new DelegateSecurityExecutionContextProvider(null));
	}

	@Test
	void exceptionsFromTheFactoryAreNotSwallowed() {
		var provider = new DelegateSecurityExecutionContextProvider(() -> {
			throw new IllegalStateException("authentication middleware failed");
		});

		var ex = assertThrows(IllegalStateException.class, provider::getSecurityExecutionContext);
		assertEquals("authentication middleware failed", ex.getMessage());
	}
}
