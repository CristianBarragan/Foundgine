package com.foundgine.providers.storage.sql.retrieval;

import com.foundgine.core.abstractions.*;
import com.foundgine.core.semantic.metadata.*;
import com.foundgine.core.semantic.resolution.*;

import javax.sql.DataSource;
import java.sql.*;
import java.util.*;

/**
 * PostgreSQL implementation of the semantic candidate boundary.
 *
 * <p>
 * Retrieval is advisory evidence only. Semantic resolution and authorization
 * remain outside the physical retrieval provider.
 * </p>
 */
public final class PostgresRetrievalCandidateSource implements IApproximateCandidateSource {
	private final DataSource dataSource;
	private final IMetadataCatalog metadata;
	private final PostgresRetrievalOptions options;

	public PostgresRetrievalCandidateSource(DataSource dataSource, IMetadataCatalog metadata) {
		this(dataSource, metadata, new PostgresRetrievalOptions());
	}

	public PostgresRetrievalCandidateSource(DataSource dataSource, IMetadataCatalog metadata,
			PostgresRetrievalOptions options) {
		this.dataSource = Objects.requireNonNull(dataSource, "dataSource");
		this.metadata = Objects.requireNonNull(metadata, "metadata");
		this.options = options == null ? new PostgresRetrievalOptions() : options;
	}

	@Override
	public List<RetrievalCandidate> retrieve(SemanticRetrievalRequest request) {
		Objects.requireNonNull(request, "request");
		EntityMetadata entity = metadata.getEntity(request.entityType());

		return switch (request.strategy()) {
		case FUZZY -> {
			requireEnabled(options.enablePgTrgm(), RetrievalStrategy.FUZZY);
			yield executeText(entity, resolveField(entity, request.field()), request, CandidateEvidenceKind.TRIGRAM,
					fuzzySql(entity, resolveField(entity, request.field())));
		}
		case FULL_TEXT -> {
			requireEnabled(options.enableFullText(), RetrievalStrategy.FULL_TEXT);
			yield executeText(entity, resolveField(entity, request.field()), request, CandidateEvidenceKind.FULL_TEXT,
					fullTextSql(entity, resolveField(entity, request.field())));
		}
		case SEARCH -> {
			requireEnabled(options.enablePgSearch(), RetrievalStrategy.SEARCH);
			yield executeText(entity, resolveField(entity, request.field()), request, CandidateEvidenceKind.BM25,
					searchSql(entity, resolveField(entity, request.field())));
		}
		case GRAPH_SIMILARITY -> {
			requireEnabled(options.enableApacheAge(), RetrievalStrategy.GRAPH_SIMILARITY);
			yield executeGraphSimilarity(entity, request);
		}
		case RELATIONAL -> List.of();
		case VECTOR -> throw new UnsupportedOperationException(
				"This field-value retrieval boundary does not implement vector search. "
						+ "Use the pgvector semantic lexical candidate source instead.");
		default -> throw new IllegalArgumentException("Unknown retrieval strategy: " + request.strategy());
		};
	}

	private List<RetrievalCandidate> executeText(EntityMetadata entity, FieldMetadata field,
			SemanticRetrievalRequest request, CandidateEvidenceKind evidenceKind, String sql) {
		try (Connection connection = dataSource.getConnection();
				PreparedStatement statement = connection.prepareStatement(sql)) {
			int i = 1;
			statement.setString(i++, request.query());
			if (request.strategy() == RetrievalStrategy.FUZZY || request.strategy() == RetrievalStrategy.FULL_TEXT)
				statement.setString(i++, request.query());
			statement.setInt(i, request.limit());
			try (ResultSet result = statement.executeQuery()) {
				return readCandidates(result, entity.entityId(), field.id(), evidenceKind);
			}
		} catch (SQLException ex) {
			throw new IllegalStateException("PostgreSQL " + request.strategy() + " retrieval failed.", ex);
		}
	}

	private List<RetrievalCandidate> executeGraphSimilarity(EntityMetadata entity, SemanticRetrievalRequest request) {
		if (request.relationship() == null)
			throw new IllegalArgumentException("GraphSimilarity requires Relationship.");
		if (request.referenceIdentity() == null || request.referenceIdentity().isBlank())
			throw new IllegalArgumentException("GraphSimilarity requires ReferenceIdentity.");

		RelationshipMetadata relationship = metadata.getRelationship(request.relationship());
		EntityMetadata source = metadata.getEntity(relationship.source());
		EntityMetadata target = metadata.getEntity(relationship.target());
		String sourceLabel = quoteIdentifier(source.effectiveStorageName());
		String edgeLabel = quoteIdentifier(relationship.name());
		String reference = request.referenceIdentity().replace("\\", "\\\\").replace("\"", "\\\"");
		String cypher = "MATCH (reference:" + sourceLabel + ")-[:" + edgeLabel + "]->(neighbor) " + "<-[:" + edgeLabel
				+ "]-(candidate:" + sourceLabel + ") " + "WHERE reference.id = \"" + reference
				+ "\" AND candidate.id <> \"" + reference + "\" " + "RETURN candidate.id, count(*) AS score "
				+ "ORDER BY score DESC LIMIT " + request.limit();

		String sql = "SELECT * FROM cypher(" + quoteLiteral(options.ageGraphName()) + ", ?) "
				+ "AS (record_id text, score bigint)";
		try (Connection connection = dataSource.getConnection(); Statement load = connection.createStatement()) {
			load.execute("LOAD 'age'");
			try (PreparedStatement command = connection.prepareStatement(sql)) {
				command.setString(1, cypher);
				try (ResultSet result = command.executeQuery()) {
					return readCandidates(result, entity.entityId(), null, CandidateEvidenceKind.GRAPH_SIMILARITY);
				}
			}
		} catch (SQLException ex) {
			throw new IllegalStateException("PostgreSQL GraphSimilarity retrieval failed.", ex);
		}
	}

	private static List<RetrievalCandidate> readCandidates(ResultSet result, EntityId entityType, FieldId field,
			CandidateEvidenceKind kind) throws SQLException {
		List<RetrievalCandidate> candidates = new ArrayList<>();
		while (result.next()) {
			String recordId = Objects.toString(result.getObject(1), "");
			double score = result.getObject(2) == null ? 0d : result.getDouble(2);
			candidates.add(new RetrievalCandidate(entityType, recordId, score, field, recordId,
					List.of(new ResolutionEvidence(kind + " retrieval matched semantic candidate " + recordId
							+ " with score " + formatScore(score) + ".", kind, score)),
					kind));
		}
		return List.copyOf(candidates);
	}

	private FieldMetadata resolveField(EntityMetadata entity, FieldId fieldId) {
		if (fieldId != null) {
			return entity.effectiveFields().stream().filter(x -> x.id().equals(fieldId)).findFirst()
					.orElseThrow(() -> new NoSuchElementException(
							"Field " + fieldId + " is not registered on entity " + entity.name() + "."));
		}
		return entity.effectiveFields().stream().filter(x -> x.clrType() == String.class && x.column() != null)
				.findFirst().orElseThrow(() -> new IllegalStateException(
						"Entity " + entity.name() + " has no string field available for approximate retrieval."));
	}

	private String fuzzySql(EntityMetadata entity, FieldMetadata field) {
		String table = quoteStorage(entity.effectiveStorageName());
		String column = quoteIdentifier(getColumnName(entity, field));
		String identity = quoteIdentifier(getPrimaryKeyName(entity));
		return "SELECT " + identity + ", similarity(" + column + ", ?) AS score " + "FROM " + table + " WHERE " + column
				+ " % ? ORDER BY score DESC LIMIT ?";
	}

	private String fullTextSql(EntityMetadata entity, FieldMetadata field) {
		String table = quoteStorage(entity.effectiveStorageName());
		String column = quoteIdentifier(getColumnName(entity, field));
		String identity = quoteIdentifier(getPrimaryKeyName(entity));
		String config = quoteLiteral(options.fullTextConfiguration());
		return "SELECT " + identity + ", ts_rank_cd(to_tsvector(" + config + ", COALESCE(" + column + "::text, '')), "
				+ "websearch_to_tsquery(" + config + ", ?)) AS score FROM " + table + " WHERE to_tsvector(" + config
				+ ", COALESCE(" + column + "::text, '')) @@ " + "websearch_to_tsquery(" + config
				+ ", ?) ORDER BY score DESC LIMIT ?";
	}

	private String searchSql(EntityMetadata entity, FieldMetadata field) {
		String table = quoteStorage(entity.effectiveStorageName());
		String column = quoteIdentifier(getColumnName(entity, field));
		String identity = quoteIdentifier(getPrimaryKeyName(entity));
		return "SELECT " + identity + ", pdb.score(" + identity + ") AS score FROM " + table + " WHERE " + column
				+ " ||| ? ORDER BY score DESC LIMIT ?";
	}

	private String getColumnName(EntityMetadata entity, FieldMetadata field) {
		if (field.column() == null)
			throw new IllegalStateException("Field " + field.name() + " has no storage column.");
		return entity.columns().stream().filter(x -> x.id().equals(field.column().columnId())).findFirst()
				.orElseThrow(() -> new IllegalStateException("Column " + field.column().columnId() + " for field "
						+ field.name() + " is not registered on entity " + entity.name() + "."))
				.effectiveStorageName();
	}

	private String getPrimaryKeyName(EntityMetadata entity) {
		if (entity.primaryKey() == null)
			throw new IllegalStateException("Entity " + entity.name() + " has no primary key metadata.");
		return entity.columns().stream().filter(x -> x.id().equals(entity.primaryKey().columnId())).findFirst()
				.orElseThrow(() -> new IllegalStateException("Primary key column " + entity.primaryKey().columnId()
						+ " is not registered on entity " + entity.name() + "."))
				.effectiveStorageName();
	}

	private static void requireEnabled(boolean enabled, RetrievalStrategy strategy) {
		if (!enabled)
			throw new UnsupportedOperationException("PostgreSQL retrieval strategy " + strategy + " is disabled.");
	}

	private static String quoteStorage(String value) {
		String[] parts = value.split("\\.");
		if (parts.length == 0)
			throw new IllegalArgumentException("Storage name cannot be empty.");
		return Arrays.stream(parts).filter(x -> !x.isBlank()).map(PostgresRetrievalCandidateSource::quoteIdentifier)
				.reduce((a, b) -> a + "." + b).orElseThrow();
	}

	private static String quoteIdentifier(String value) {
		return "\"" + value.replace("\"", "\"\"") + "\"";
	}

	private static String quoteLiteral(String value) {
		return "'" + value.replace("'", "''") + "'";
	}

	private static String formatScore(double score) {
		return String.format(Locale.ROOT, "%.4f", score);
	}
}
