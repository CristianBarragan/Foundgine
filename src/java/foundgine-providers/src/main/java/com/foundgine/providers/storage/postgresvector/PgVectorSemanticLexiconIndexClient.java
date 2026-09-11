package com.foundgine.providers.storage.postgresvector;

import com.foundgine.core.semantic.*;
import com.foundgine.core.semantic.resolution.*;
import java.sql.*;
import java.util.*;
import javax.sql.DataSource;

/** Maintains the derived pgvector semantic lexicon projection. */
public final class PgVectorSemanticLexiconIndexClient {
    private final DataSource ds;
    private final PgVectorSemanticLexicalCandidateSource.EmbeddingGenerator embeddings;
    private final PgVectorOptions options;

    public PgVectorSemanticLexiconIndexClient(DataSource d, PgVectorSemanticLexicalCandidateSource.EmbeddingGenerator e) {
        this(d, e, null);
    }
    public PgVectorSemanticLexiconIndexClient(DataSource d, PgVectorSemanticLexicalCandidateSource.EmbeddingGenerator e, PgVectorOptions o) {
        ds = Objects.requireNonNull(d);
        embeddings = Objects.requireNonNull(e);
        options = o == null ? new PgVectorOptions() : o;
    }

    public void ensureSchema() {
        String table = options.qualifiedTableName();
        try (Connection c = ds.getConnection(); Statement s = c.createStatement()) {
            s.execute("CREATE EXTENSION IF NOT EXISTS vector");
            s.execute("CREATE TABLE IF NOT EXISTS " + table + " (" +
                "id bigserial primary key,canonical_name text not null,kind text not null,search_text text not null," +
                "aliases text[] not null default ARRAY[]::text[],description text,entity_id bigint,relationship_id bigint," +
                "field_id bigint,source_entity_id bigint,target_entity_id bigint,value text,embedding vector(" + options.dimensions() + "))");
            s.execute("CREATE INDEX IF NOT EXISTS " + identifier(options.tableName() + "_kind_idx") + " ON " + table + " (kind)");
            s.execute("CREATE INDEX IF NOT EXISTS " + identifier(options.tableName() + "_embedding_hnsw_idx") +
                " ON " + table + " USING hnsw (embedding " + VectorOpsClass(options.distance()) + ")");
        } catch (SQLException e) { throw new IllegalStateException("Unable to create pgvector lexicon", e); }
    }

    public void indexContract(SemanticContractSnapshot contract) {
        Objects.requireNonNull(contract);
        ensureSchema();
        List<SemanticLexiconEntry> entries = SemanticLexiconProjection.build(contract);
        List<float[]> vectors = embeddings.embedMany(entries.stream().map(SemanticLexiconEntry::searchText).toList());
        if (vectors.size() != entries.size()) throw new IllegalStateException("Embedding generator returned a different number of vectors than lexicon entries.");
        try (Connection c = ds.getConnection()) {
            c.setAutoCommit(false);
            try {
                try (Statement s = c.createStatement()) { s.executeUpdate("TRUNCATE TABLE " + options.qualifiedTableName()); }
                for (int i = 0; i < entries.size(); i++) insert(c, entries.get(i), vectors.get(i));
                c.commit();
            } catch (SQLException | RuntimeException ex) {
                try { c.rollback(); } catch (SQLException ignored) { }
                throw ex;
            } finally { try { c.setAutoCommit(true); } catch (SQLException ignored) { } }
        } catch (SQLException e) { throw new IllegalStateException("Unable to index pgvector lexicon", e); }
    }

    public void indexEntry(SemanticLexiconEntry entry) {
        Objects.requireNonNull(entry);
        float[] vector = embeddings.embed(entry.searchText());
        try (Connection c = ds.getConnection()) { insert(c, entry, vector); }
        catch (SQLException e) { throw new IllegalStateException("Unable to index pgvector lexicon entry", e); }
    }

    private void insert(Connection c, SemanticLexiconEntry e, float[] vector) throws SQLException {
        String sql = "INSERT INTO " + options.qualifiedTableName() +
            "(canonical_name,kind,search_text,aliases,description,entity_id,relationship_id,field_id,source_entity_id,target_entity_id,value,embedding) VALUES(?,?,?,?,?,?,?,?,?,?,?,?::vector)";
        try (PreparedStatement p = c.prepareStatement(sql)) {
            p.setString(1, e.canonicalName()); p.setString(2, e.kind().toString()); p.setString(3, e.searchText());
            p.setArray(4, c.createArrayOf("text", e.effectiveAliases().toArray(String[]::new)));
            p.setString(5, e.description());
            p.setObject(6, e.entityId() == null ? null : e.entityId().value());
            p.setObject(7, e.relationshipId() == null ? null : e.relationshipId().value());
            p.setObject(8, e.fieldId() == null ? null : e.fieldId().value());
            p.setObject(9, e.sourceEntityId() == null ? null : e.sourceEntityId().value());
            p.setObject(10, e.targetEntityId() == null ? null : e.targetEntityId().value());
            p.setString(11, e.value()); p.setString(12, vectorLiteral(vector)); p.executeUpdate();
        }
    }
    static String VectorOpsClass(PgVectorDistance distance) {
        return switch (distance) { case COSINE -> "vector_cosine_ops"; case L2 -> "vector_l2_ops"; case INNER_PRODUCT -> "vector_ip_ops"; };
    }
    private static String vectorLiteral(float[] vector) {
        if (vector == null || vector.length == 0) throw new IllegalArgumentException("Embedding vector cannot be empty.");
        StringBuilder b = new StringBuilder("["); for (int i=0;i<vector.length;i++){if(i>0)b.append(',');b.append(Float.toString(vector[i]));} return b.append(']').toString();
    }
    private static String identifier(String value) { return "\\\"" + value.replace("\\\"", "\\\"\\\"") + "\\\""; }
}
