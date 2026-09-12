package com.foundgine.providers.storage.postgresvector;

import com.foundgine.core.abstractions.*;
import com.foundgine.core.semantic.resolution.*;
import java.sql.*;
import java.util.*;
import javax.sql.DataSource;

/**
 * pgvector implementation of Foundgine's provider-neutral lexical candidate
 * source. pgvector proposes ranked hypotheses; the semantic contract remains
 * authoritative for meaning, topology, and authorization.
 */
public final class PgVectorSemanticLexicalCandidateSource implements ISemanticLexicalCandidateSource {
	public interface EmbeddingGenerator {
		float[] embed(String token);

		default List<float[]> embedMany(List<String> texts) {
			return texts.stream().map(this::embed).toList();
		}
	}

	private final DataSource dataSource;
	private final EmbeddingGenerator embeddings;
	private final PgVectorOptions options;

	public PgVectorSemanticLexicalCandidateSource(DataSource ds, EmbeddingGenerator eg) {
		this(ds, eg, null);
	}

	public PgVectorSemanticLexicalCandidateSource(DataSource ds, EmbeddingGenerator eg, PgVectorOptions o) {
		dataSource = Objects.requireNonNull(ds);
		embeddings = Objects.requireNonNull(eg);
		options = o == null ? new PgVectorOptions() : o;
	}

	@Override
	public List<SemanticLexicalCandidate> retrieve(SemanticLexicalRequest request) {
		Objects.requireNonNull(request);
		float[] vector = embeddings.embed(request.token());
		String distanceOperator = DistanceOperator(options.distance());
		List<SemanticLexicalCandidateKind> kinds = request.effectiveKinds();
		StringBuilder sql = new StringBuilder(
				"SELECT canonical_name,kind,entity_id,relationship_id,field_id,source_entity_id,target_entity_id,value,embedding ")
				.append(distanceOperator).append(" ?::vector AS distance FROM ").append(options.qualifiedTableName())
				.append(" WHERE kind = ANY(?)");
		if (request.contextEntity() != null)
			sql.append(" AND (entity_id = ? OR source_entity_id = ? OR target_entity_id = ?)");
		sql.append(" ORDER BY embedding ").append(distanceOperator).append(" ?::vector LIMIT ?");

		try (Connection c = dataSource.getConnection(); PreparedStatement p = c.prepareStatement(sql.toString())) {
			String vectorParameter = vectorLiteral(vector);
			int i = 1;
			p.setString(i++, vectorParameter);
			p.setArray(i++, c.createArrayOf("text", kinds.stream().map(Enum::toString).toArray(String[]::new)));
			if (request.contextEntity() != null) {
				long id = request.contextEntity().value();
				p.setLong(i++, id);
				p.setLong(i++, id);
				p.setLong(i++, id);
			}
			p.setString(i++, vectorParameter);
			p.setInt(i, request.limit());
			try (ResultSet rs = p.executeQuery()) {
				List<SemanticLexicalCandidate> out = new ArrayList<>();
				while (rs.next()) {
					SemanticLexicalCandidate candidate = readCandidate(request.token(), rs, options.distance());
					if (candidate != null)
						out.add(candidate);
				}
				return out;
			}
		} catch (SQLException e) {
			throw new IllegalStateException("pgvector retrieval failed", e);
		}
	}

	static double ToScore(double distance, PgVectorDistance metric) {
		return switch (metric) {
		case COSINE -> 1d - distance;
		case L2 -> 1d / (1d + distance);
		case INNER_PRODUCT -> -distance;
		};
	}

	static String DistanceOperator(PgVectorDistance distance) {
		return switch (distance) {
		case COSINE -> "<=>";
		case L2 -> "<->";
		case INNER_PRODUCT -> "<#>";
		};
	}

	private static String vectorLiteral(float[] vector) {
		if (vector == null || vector.length == 0)
			throw new IllegalArgumentException("Embedding vector cannot be empty.");
		StringBuilder b = new StringBuilder("[");
		for (int i = 0; i < vector.length; i++) {
			if (i > 0)
				b.append(',');
			b.append(Float.toString(vector[i]));
		}
		return b.append(']').toString();
	}

	private static SemanticLexicalCandidate readCandidate(String token, ResultSet rs, PgVectorDistance metric)
			throws SQLException {
		SemanticLexicalCandidateKind kind;
		try {
			kind = SemanticLexicalCandidateKind.valueOf(rs.getString(2).toUpperCase(Locale.ROOT));
		} catch (IllegalArgumentException ex) {
			return null;
		}
		double distance = rs.getDouble(9);
		double score = ToScore(distance, metric);
		return new SemanticLexicalCandidate(token, kind, rs.getString(1), score, entityId(rs, 3), relationshipId(rs, 4),
				fieldId(rs, 5), entityId(rs, 6), entityId(rs, 7), rs.getString(8),
				List.of(new ResolutionEvidence("pgvector nearest-neighbor match for '" + token + "' (" + metric
						+ " distance " + distance + ").", CandidateEvidenceKind.VECTOR_SIMILARITY, score)));
	}

	private static EntityId entityId(ResultSet r, int i) throws SQLException {
		return r.getObject(i) == null ? null : new EntityId(r.getLong(i));
	}

	private static RelationshipId relationshipId(ResultSet r, int i) throws SQLException {
		return r.getObject(i) == null ? null : new RelationshipId(r.getLong(i));
	}

	private static FieldId fieldId(ResultSet r, int i) throws SQLException {
		return r.getObject(i) == null ? null : new FieldId(r.getLong(i));
	}
}
