package com.foundgine.providers.tools.mcp;

import com.fasterxml.jackson.databind.DeserializationFeature;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.fasterxml.jackson.databind.SerializationFeature;
import com.fasterxml.jackson.datatype.jsr310.JavaTimeModule;

/** Shared mappers of the MCP adapter. Output uses camelCase record names (the C# adapter's {@code JsonSerializerDefaults.Web}). */
final class FoundgineMcpJson {
    private FoundgineMcpJson() {}

    /** Writes tool results (receipts carry Instants, so java.time support is registered explicitly). */
    static final ObjectMapper WRITER = build(false);
    /** Reads tool input; fractional numbers stay exact (BigDecimal), like System.Text.Json's decimal. */
    static final ObjectMapper READER = build(true);

    private static ObjectMapper build(boolean exactDecimals) {
        var mapper = new ObjectMapper().registerModule(new JavaTimeModule());
        mapper.disable(SerializationFeature.WRITE_DATES_AS_TIMESTAMPS);
        if (exactDecimals) mapper.enable(DeserializationFeature.USE_BIG_DECIMAL_FOR_FLOATS);
        return mapper;
    }
}
