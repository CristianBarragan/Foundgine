package com.foundgine.providers.storage.elasticsearch;

import com.fasterxml.jackson.databind.*;
import com.foundgine.core.semantic.*;
import com.foundgine.core.semantic.resolution.*;
import java.net.*;
import java.net.http.*;
import java.util.*;

/** Indexes the derived semantic lexicon projection into Elasticsearch. */
public final class SemanticLexiconIndexClient {
	private final HttpClient client;
	private final URI base;
	private final String index;
	private final ObjectMapper mapper = new ObjectMapper();

	public SemanticLexiconIndexClient(HttpClient c, URI b) {
		this(c, b, "foundgine-semantic-lexicon");
	}

	public SemanticLexiconIndexClient(HttpClient c, URI b, String i) {
		client = Objects.requireNonNull(c);
		base = Objects.requireNonNull(b);
		if (i == null || i.isBlank())
			throw new IllegalArgumentException("index");
		index = i;
	}

	public void ensureIndex() {
		try {
			HttpRequest h = HttpRequest.newBuilder(uri()).method("HEAD", HttpRequest.BodyPublishers.noBody()).build();
			HttpResponse<String> r = client.send(h, HttpResponse.BodyHandlers.ofString());
			if (r.statusCode() == 200)
				return;
			if (r.statusCode() != 404)
				throw new IllegalStateException("Elasticsearch returned " + r.statusCode());
			Map<String, Object> props = new LinkedHashMap<>();
			for (String[] x : new String[][] { { "canonicalName", "text" }, { "kind", "keyword" },
					{ "searchText", "text" }, { "aliases", "text" }, { "description", "text" }, { "entityId", "long" },
					{ "relationshipId", "long" }, { "fieldId", "long" }, { "sourceEntityId", "long" },
					{ "targetEntityId", "long" }, { "value", "text" } })
				props.put(x[0], Map.of("type", x[1]));
			Map<String, Object> body = Map.of("mappings", Map.of("properties", props));
			send("PUT", uri(), mapper.writeValueAsString(body));
		} catch (Exception e) {
			throw new IllegalStateException("Unable to ensure Elasticsearch index", e);
		}
	}

	public void indexContract(SemanticContractSnapshot c) {
		ensureIndex();
		StringBuilder b = new StringBuilder();
		for (var e : SemanticLexiconProjection.build(c)) {
			b.append("{\"index\":{}}\n").append(write(e)).append('\n');
		}
		send("POST", base.resolve("/" + index + "/_bulk"), b.toString());
	}

	public void indexEntry(SemanticLexiconEntry e) {
		send("POST", base.resolve("/" + index + "/_doc"), write(e));
	}

	private String write(Object x) {
		try {
			return mapper.writeValueAsString(x);
		} catch (Exception e) {
			throw new IllegalStateException(e);
		}
	}

	private URI uri() {
		return base.resolve("/" + index);
	}

	private void send(String method, URI u, String body) {
		try {
			HttpRequest.Builder b = HttpRequest.newBuilder(u).header("Content-Type", "application/json");
			HttpRequest r = (method.equals("PUT") ? b.PUT(bod(body)) : b.POST(bod(body))).build();
			HttpResponse<String> x = client.send(r, HttpResponse.BodyHandlers.ofString());
			if (x.statusCode() / 100 != 2)
				throw new IllegalStateException("Elasticsearch returned " + x.statusCode() + ": " + x.body());
		} catch (Exception e) {
			if (e instanceof RuntimeException x)
				throw x;
			throw new IllegalStateException(e);
		}
	}

	private static HttpRequest.BodyPublisher bod(String s) {
		return HttpRequest.BodyPublishers.ofString(s);
	}
}
