package com.foundgine.core.abstractions;

import com.fasterxml.jackson.core.JsonGenerator;
import com.fasterxml.jackson.core.JsonParser;
import com.fasterxml.jackson.core.JsonToken;
import com.fasterxml.jackson.databind.JsonNode;

import java.io.IOException;
import java.math.BigInteger;

/**
 * Port of the shared read/write logic duplicated across every
 * {@code *IdJsonConverter} in the C# source (EntityId, FieldId, ColumnId,
 * ConnectionId, ModelId, RelationshipId, AuthorizationId). The C# types wrap a
 * {@code ulong}; Java has no unsigned 64-bit primitive, so the identity is
 * stored as a signed {@code long} using its full 64-bit bit pattern, and this
 * class is responsible for translating that bit pattern to/from the unsigned
 * decimal representation used on the wire (matching {@code System.Text.Json}'s
 * handling of {@code ulong}).
 */
public final class UnsignedLongJson {

	private UnsignedLongJson() {
	}

	/**
	 * Writes {@code value} (interpreted as unsigned) as a JSON number, e.g.
	 * {@code 18446744073709551615}.
	 */
	public static void writeNumber(JsonGenerator generator, long value) throws IOException {
		if (value >= 0) {
			generator.writeNumber(value);
		} else {
			generator.writeNumber(new BigInteger(Long.toUnsignedString(value)));
		}
	}

	/**
	 * Writes {@code value} (interpreted as unsigned) as a JSON property name, e.g.
	 * a map key.
	 */
	public static void writeFieldName(JsonGenerator generator, long value) throws IOException {
		generator.writeFieldName(Long.toUnsignedString(value));
	}

	/**
	 * Reads either a bare unsigned-integer JSON number, or the legacy
	 * {@code {"Value": <number>}} object shape emitted by older Foundgine versions.
	 * Mirrors {@code *IdJsonConverter.Read} in the C# source.
	 */
	public static long read(JsonParser parser, String typeName) throws IOException {
		if (parser.currentToken() == JsonToken.VALUE_NUMBER_INT) {
			return parser.getBigIntegerValue().longValue();
		}

		if (parser.currentToken() == JsonToken.START_OBJECT) {
			JsonNode node = parser.getCodec().readTree(parser);
			JsonNode value = node.get("Value");
			if (value != null && value.isIntegralNumber()) {
				return value.bigIntegerValue().longValue();
			}
		}

		throw new IllegalArgumentException(
				"Expected a " + typeName + " numeric value or a legacy {\"Value\":...} object.");
	}

	/**
	 * Parses an unsigned decimal string (e.g. a JSON property name used as a map
	 * key) into its bit pattern.
	 */
	public static long parseUnsigned(String text) {
		return Long.parseUnsignedLong(text);
	}

	/**
	 * Renders {@code value} (interpreted as unsigned) the way
	 * {@code ulong.ToString()} would.
	 */
	public static String toUnsignedString(long value) {
		return Long.toUnsignedString(value);
	}
}
