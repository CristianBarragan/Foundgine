package com.foundgine.providers.storage.elasticsearch;

import com.fasterxml.jackson.databind.*;
import com.foundgine.core.abstractions.*;
import com.foundgine.core.semantic.resolution.*;
import java.net.*;
import java.net.http.*;
import java.util.*;

/**
 * Elasticsearch lexical retrieval using the REST API; context is a retrieval
 * hint only.
 */
public final class ElasticsearchSemanticLexicalCandidateSource implements ISemanticLexicalCandidateSource {
	private final HttpClient client;
	private final URI base;
	private final String index;
	private final ObjectMapper mapper;

	public ElasticsearchSemanticLexicalCandidateSource(HttpClient c, URI base, String index) {
		client = Objects.requireNonNull(c);
		this.base = Objects.requireNonNull(base);
		this.index = Objects.requireNonNull(index);
		mapper = new ObjectMapper();
	}

	@Override
	public List<SemanticLexicalCandidate> retrieve(SemanticLexicalRequest r) {
		try {
			Map<String, Object> mm = Map.of("query", r.token(), "fields",
					List.of("canonicalName^4", "aliases^3", "searchText^2", "description"), "fuzziness", "AUTO");
			Map<String, Object> body = new LinkedHashMap<>();
			body.put("size", r.limit());
			body.put("query", Map.of("bool", Map.of("must", List.of(Map.of("multi_match", mm)), "filter", List
					.of(Map.of("terms", Map.of("kind", r.effectiveKinds().stream().map(Enum::toString).toList()))))));
			HttpRequest req = HttpRequest.newBuilder(base.resolve("/" + URLEncoder.encode(index, "UTF-8") + "/_search"))
					.header("Content-Type", "application/json")
					.POST(HttpRequest.BodyPublishers.ofString(mapper.writeValueAsString(body))).build();
			HttpResponse<String> res = client.send(req, HttpResponse.BodyHandlers.ofString());
			if (res.statusCode() / 100 != 2)
				throw new IllegalStateException("Elasticsearch returned " + res.statusCode());
			JsonNode hits = mapper.readTree(res.body()).path("hits").path("hits");
			List<SemanticLexicalCandidate> out = new ArrayList<>();
			for (JsonNode h : hits) {
				JsonNode s = h.path("_source");
				if (s.path("canonicalName").isMissingNode())
					continue;
				SemanticLexicalCandidateKind k = SemanticLexicalCandidateKind
						.valueOf(s.path("kind").asText().toUpperCase(Locale.ROOT));
				double score = h.path("_score").asDouble();
				out.add(new SemanticLexicalCandidate(r.token(), k, s.path("canonicalName").asText(), score,
						uid(s, "entityId"), rid(s, "relationshipId"), fid(s, "fieldId"), uid(s, "sourceEntityId"),
						uid(s, "targetEntityId"), s.path("value").isMissingNode() ? null : s.path("value").asText(),
						List.of(new ResolutionEvidence("Elasticsearch lexical match", CandidateEvidenceKind.BM25,
								score))));
			}
			return out;
		} catch (Exception e) {
			if (e instanceof RuntimeException x)
				throw x;
			throw new IllegalStateException("Elasticsearch retrieval failed", e);
		}
	}

	private static EntityId uid(JsonNode n, String x) {
		return n.hasNonNull(x) ? new EntityId(n.get(x).asLong()) : null;
	}

	private static RelationshipId rid(JsonNode n, String x) {
		return n.hasNonNull(x) ? new RelationshipId(n.get(x).asLong()) : null;
	}

	private static FieldId fid(JsonNode n, String x) {
		return n.hasNonNull(x) ? new FieldId(n.get(x).asLong()) : null;
	}
}
