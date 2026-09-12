package com.foundgine.core.semantic.resolution;

import com.foundgine.core.abstractions.*;
import com.foundgine.core.semantic.*;
import java.util.*;

/**
 * Grounds a natural-language reference against the semantic model before data
 * retrieval. Entity/field aliases narrow the search space; provider-backed
 * retrieval contributes ranked data evidence. No provider-specific search
 * technology leaks into this layer.
 */
public final class SemanticReferenceGrounder {
	private final SemanticModel model;
	private final IApproximateCandidateSource candidates;

	public SemanticReferenceGrounder(SemanticModel model, IApproximateCandidateSource candidates) {
		this.model = Objects.requireNonNull(model);
		this.candidates = Objects.requireNonNull(candidates);
	}

	public List<SemanticReferenceEvidence> ground(String query) {
		return ground(query, RetrievalStrategy.SEARCH, 5, 10);
	}

	public List<SemanticReferenceEvidence> ground(String query, RetrievalStrategy strategy) {
		return ground(query, strategy, 5, 10);
	}

	public List<SemanticReferenceEvidence> ground(String query, RetrievalStrategy strategy, int entityLimit,
			int candidateLimit) {
		if (query == null || query.isBlank())
			throw new IllegalArgumentException("Reference query cannot be empty.");
		if (entityLimit < 1)
			throw new IllegalArgumentException("entityLimit must be positive.");
		if (candidateLimit < 1)
			throw new IllegalArgumentException("candidateLimit must be positive.");

		var matches = model.entities().stream().map(entity -> new EntityScore(entity, entityScore(entity, query)))
				.filter(x -> x.entity().fields().stream()
						.anyMatch(field -> field.clrType() == String.class
								&& (field.capabilities() & SemanticFieldCapabilities.SENSITIVE) == 0))
				.sorted(Comparator.comparingDouble(EntityScore::score).reversed().thenComparing(x -> x.entity().name(),
						String.CASE_INSENSITIVE_ORDER))
				.limit(entityLimit).toList();

		var results = new ArrayList<SemanticReferenceEvidence>();
		for (var match : matches) {
			var field = selectSearchField(match.entity(), query);
			if (field == null)
				continue;
			var retrieval = candidates.retrieve(
					new SemanticRetrievalRequest(match.entity().id(), field.id(), query, strategy, candidateLimit));
			if (retrieval == null || retrieval.isEmpty())
				continue;
			var confidence = clamp((match.score() * 0.35d) + (retrieval.get(0).score() * 0.65d));
			results.add(new SemanticReferenceEvidence(query, retrieval, confidence,
					"'" + query + "' grounded against semantic entity '" + match.entity().name() + "' and field '"
							+ field.name() + "'."));
		}
		return results.stream().sorted(Comparator.comparingDouble(SemanticReferenceEvidence::confidence).reversed())
				.toList();
	}

	private static double entityScore(SemanticEntity entity, String query) {
		var entityScore = similarity(query, entity.name());
		var aliasScore = entity.effectiveAliases().stream().mapToDouble(a -> similarity(query, a.name())).max()
				.orElse(0d);
		var fieldScore = entity.fields().stream()
				.flatMap(f -> java.util.stream.Stream.concat(java.util.stream.Stream.of(f.name()),
						f.effectiveAliases().stream().map(SemanticAlias::name)))
				.mapToDouble(n -> similarity(query, n)).max().orElse(0d);
		return Math.max(entityScore, Math.max(aliasScore, fieldScore * 0.9d));
	}

	private static SemanticField selectSearchField(SemanticEntity entity, String query) {
		return entity.fields().stream()
				.filter(field -> field.clrType() == String.class
						&& (field.capabilities() & SemanticFieldCapabilities.SENSITIVE) == 0)
				.map(field -> new FieldScore(field,
						0.5d + (0.5d * Math.max(similarity(query, field.name()),
								field.effectiveAliases().stream().mapToDouble(a -> similarity(query, a.name())).max()
										.orElse(0d)))))
				.sorted(Comparator.comparingDouble(FieldScore::score).reversed().thenComparing(x -> x.field().name(),
						String.CASE_INSENSITIVE_ORDER))
				.map(FieldScore::field).findFirst().orElse(null);
	}

	private static double similarity(String left, String right) {
		if (left.equalsIgnoreCase(right))
			return 1d;
		if (containsIgnoreCase(left, right) || containsIgnoreCase(right, left))
			return 0.8d;
		var max = Math.max(left.length(), right.length());
		if (max == 0)
			return 1d;
		return 1d - ((double) levenshtein(left, right) / max);
	}

	private static boolean containsIgnoreCase(String a, String b) {
		return a.toLowerCase(Locale.ROOT).contains(b.toLowerCase(Locale.ROOT));
	}

	private static int levenshtein(String a, String b) {
		var previous = new int[b.length() + 1];
		for (int i = 0; i <= b.length(); i++)
			previous[i] = i;
		for (int i = 1; i <= a.length(); i++) {
			var current = new int[b.length() + 1];
			current[0] = i;
			for (int j = 1; j <= b.length(); j++)
				current[j] = Math.min(Math.min(current[j - 1] + 1, previous[j] + 1), previous[j - 1]
						+ (Character.toUpperCase(a.charAt(i - 1)) == Character.toUpperCase(b.charAt(j - 1)) ? 0 : 1));
			previous = current;
		}
		return previous[b.length()];
	}

	private static double clamp(double value) {
		return Math.max(0d, Math.min(1d, value));
	}

	private record EntityScore(SemanticEntity entity, double score) {
	}

	private record FieldScore(SemanticField field, double score) {
	}
}
