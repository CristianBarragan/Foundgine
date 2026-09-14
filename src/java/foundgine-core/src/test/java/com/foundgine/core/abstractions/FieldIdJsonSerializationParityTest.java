package com.foundgine.core.abstractions;

import com.fasterxml.jackson.databind.ObjectMapper;
import org.junit.jupiter.api.Test;

import java.util.Map;

import static org.junit.jupiter.api.Assertions.*;

/**
 * Port of C# {@code Foundgine.E2E.Tests.FieldIdJsonSerializationTests}.
 *
 * <p>
 * The C# original is regression coverage for a bug where mutation execution
 * results ({@code IReadOnlyDictionary<FieldId, object?> ReturnedValues}, see
 * {@code MutationResult}) threw {@code NotSupportedException} the moment they
 * were serialized, because {@code System.Text.Json}'s default converter for a
 * struct can't double as a dictionary-key converter without explicitly
 * overriding the property-name read/write hooks.
 *
 * <p>
 * The Java port targets the equivalent Jackson failure mode: a
 * {@link com.fasterxml.jackson.databind.annotation.JsonSerialize}/{@code
 * JsonDeserialize}-annotated record used as a plain value serializes fine by
 * default, but used as a {@code Map} key needs {@code keyUsing}/{@code
 * keyUsing} deserializer wiring or it fails — see {@link FieldId.KeySerializer}
 * and {@link FieldId.KeyDeserializer}. {@link
 * com.foundgine.core.execution.mutation.MutationResult#returnedValues()} is
 * exactly this shape ({@code Map<FieldId, Object>}), so this is not a
 * hypothetical: it's the same class of bug the C# regression guards against.
 */
class FieldIdJsonSerializationParityTest {

	private final ObjectMapper mapper = new ObjectMapper();

	@Test
	void fieldIdSerializesAsAPlainNumericValue() throws Exception {
		String json = mapper.writeValueAsString(new FieldId(7));

		assertEquals("7", json);
	}

	@Test
	void fieldIdRoundTripsAsAPlainValue() throws Exception {
		String json = mapper.writeValueAsString(new FieldId(42));

		FieldId result = mapper.readValue(json, FieldId.class);

		assertEquals(new FieldId(42), result);
	}

	@Test
	void dictionaryKeyedByFieldIdSerializesWithoutThrowing() {
		// The exact shape that crashed in C#: a Map keyed by the identity
		// type, nested inside an object with other properties - matching
		// MutationResult.returnedValues() as returned to a GraphQL/MCP caller.
		Map<FieldId, Object> values = Map.of(
				new FieldId(1), "TRK-1'; DROP TABLE shipments; --",
				new FieldId(2), 42);
		Map<String, Object> wrapped = Map.of("shipment", Map.of("returnedValues", values));

		assertDoesNotThrow(() -> mapper.writeValueAsString(wrapped));
	}

	@Test
	void dictionaryKeyedByFieldIdRoundTripsThroughSerialization() throws Exception {
		Map<FieldId, Object> values = Map.of(new FieldId(3), "some-value");

		String json = mapper.writeValueAsString(values);
		Map<FieldId, Object> result = mapper.readValue(json,
				mapper.getTypeFactory().constructMapType(Map.class, FieldId.class, Object.class));

		assertNotNull(result);
		assertTrue(result.containsKey(new FieldId(3)));
	}

	@Test
	void dictionaryKeyedByFieldIdPreservesArbitraryStringValuesIncludingQuotes() throws Exception {
		// Guards specifically against the injection-payload class of value
		// (embedded single quotes) getting mangled or dropped rather than
		// merely escaped on the wire.
		String payload = "TRK-1'; DROP TABLE shipments; --";
		Map<FieldId, Object> values = Map.of(new FieldId(9), payload);

		String json = mapper.writeValueAsString(values);
		Map<FieldId, Object> result = mapper.readValue(json,
				mapper.getTypeFactory().constructMapType(Map.class, FieldId.class, Object.class));

		assertEquals(payload, result.get(new FieldId(9)).toString());
	}
}
