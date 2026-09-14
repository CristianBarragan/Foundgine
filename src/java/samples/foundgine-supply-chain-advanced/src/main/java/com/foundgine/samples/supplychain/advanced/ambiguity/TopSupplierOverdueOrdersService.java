package com.foundgine.samples.supplychain.advanced.ambiguity;

import java.math.BigDecimal;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.sql.Connection;
import java.sql.PreparedStatement;
import java.sql.ResultSetMetaData;
import java.sql.SQLException;
import java.time.LocalDate;
import java.time.temporal.ChronoUnit;
import java.util.ArrayList;
import java.util.Comparator;
import java.util.HexFormat;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.Objects;

/**
 * Java parity port of the C# advanced sample's ambiguity-resolution demo
 * capability, {@code find_top_supplier_overdue_orders} in
 * {@code MCP.Foundgine/Program.cs} (backed by the schema in
 * {@code Database/Program.cs}).
 *
 * <p>
 * "top supplier in &lt;state&gt;" is not a database key, so it is resolved
 * through ranked candidates plus evidence, not silently guessed: an
 * unambiguous top-by-value supplier resolves and executes; a tie at the top,
 * or a supplier name that doesn't match anything, comes back as
 * {@code clarification_needed}/{@code not_found} with candidates and
 * evidence instead of a guess. When the caller (agent) has already been told
 * candidates are tied and comes back with a specific name, that closes the
 * loop into a resolved result instead of asking again.
 * </p>
 *
 * <p>
 * <b>Known simplification vs. the C# original:</b> the C# version stamps
 * every resolved response with a real {@code SemanticPlan}/execution-IR
 * fingerprint from the full planner + authorizer + {@code
 * ExecutionIRCompiler} chain. Wiring that whole chain for this one demo
 * capability was out of scope for this port; {@link #planFingerprint()}
 * instead returns a stable hash of the (fixed) Supplier read shape this
 * capability always executes. The externally-visible shape of the response
 * ({@code plan} as an opaque short string) is preserved even though its
 * provenance is weaker.
 * </p>
 */
public final class TopSupplierOverdueOrdersService {
    private final Connection connection;

    public TopSupplierOverdueOrdersService(Connection connection) {
        this.connection = Objects.requireNonNull(connection, "connection");
    }

    public Map<String, Object> findTopSupplierOverdueOrders(String actor, String state, String supplierName)
            throws SQLException {
        if (state == null || state.isBlank())
            throw new IllegalArgumentException("State is required.");

        var candidates = candidatesForState(state);
        if (candidates.isEmpty()) {
            var response = new LinkedHashMap<String, Object>();
            response.put("status", "not_found");
            response.put("state", state);
            response.put("reason", "No suppliers found in state '" + state + "'.");
            return response;
        }

        Map<String, Object> supplier;
        String resolvedBy;

        if (supplierName != null && !supplierName.isBlank()) {
            // The caller is closing the loop: a prior call told them the
            // candidates were tied, and they came back with a specific
            // name instead of leaving Foundgine to guess. Still validated
            // against the real candidate set for the state - a name that
            // doesn't match anything in scope is refused, not guessed at.
            var named = candidates.stream()
                    .filter(x -> supplierName.equalsIgnoreCase((String) x.get("supplier_name")))
                    .findFirst();

            if (named.isEmpty()) {
                // Exact match failed. Before answering not_found, ask
                // whether the name is only slightly off - a typo, different
                // word order, a partial phrase - via the same approximate
                // retrieval strategies PostgresRetrievalCandidateSource
                // exposes as RetrievalStrategy.Fuzzy/FullText/Search. A hit
                // here is evidence for a "did you mean" clarification, not
                // an auto-resolve: it still hands candidates back to the
                // caller instead of binding to a guess.
                var approx = tryApproximateSupplierMatch(state, supplierName);

                if (approx != null && !approx.matches().isEmpty()) {
                    var response = new LinkedHashMap<String, Object>();
                    response.put("status", "clarification_needed");
                    response.put("state", state);
                    response.put("reason", "'" + supplierName + "' does not exactly match any supplier in state '"
                            + state + "', but " + approx.matches().size() + " looked similar via "
                            + approx.strategy() + " retrieval.");
                    response.put("candidates", approx.matches().stream().map(x -> {
                        Map<String, Object> row = new LinkedHashMap<>();
                        row.put("id", x.get("supplier_id"));
                        row.put("name", x.get("supplier_name"));
                        row.put("state", x.get("state"));
                        row.put("totalOrderValue", x.get("total_order_value"));
                        row.put("score", x.get("score"));
                        return row;
                    }).toList());
                    var evidence = new LinkedHashMap<String, Object>();
                    evidence.put("strategy", approx.strategy());
                    evidence.put("matchedAgainst", supplierName);
                    evidence.put("tie", false);
                    response.put("evidence", evidence);
                    response.put("suggestedRefinements", List.of(
                            "Re-call with the exact supplierName from the candidates above.",
                            "Narrow to a more specific region than the state."));
                    return response;
                }

                var response = new LinkedHashMap<String, Object>();
                response.put("status", "not_found");
                response.put("state", state);
                response.put("reason", "'" + supplierName + "' does not match any supplier in state '" + state
                        + "'.");
                response.put("strategiesTried", pgSearchEnabled()
                        ? List.of("exact", "fuzzy", "fulltext", "search")
                        : List.of("exact", "fuzzy", "fulltext"));
                response.put("candidates", candidates.stream().map(x -> {
                    Map<String, Object> row = new LinkedHashMap<>();
                    row.put("id", x.get("supplier_id"));
                    row.put("name", x.get("supplier_name"));
                    row.put("state", x.get("state"));
                    row.put("totalOrderValue", x.get("total_order_value"));
                    return row;
                }).toList());
                return response;
            }

            supplier = named.get();
            resolvedBy = "explicit-name";
        } else {
            BigDecimal topValue = (BigDecimal) candidates.get(0).get("total_order_value");
            var tiedAtTop = candidates.stream()
                    .filter(x -> topValue.compareTo((BigDecimal) x.get("total_order_value")) == 0)
                    .toList();

            if (tiedAtTop.size() > 1) {
                var response = new LinkedHashMap<String, Object>();
                response.put("status", "clarification_needed");
                response.put("state", state);
                response.put("reason", tiedAtTop.size() + " suppliers are tied for 'top' by total order value in "
                        + "state '" + state + "'; the request cannot be resolved to one supplier without more "
                        + "specific intent.");
                response.put("candidates", tiedAtTop.stream().map(x -> {
                    Map<String, Object> row = new LinkedHashMap<>();
                    row.put("id", x.get("supplier_id"));
                    row.put("name", x.get("supplier_name"));
                    row.put("state", x.get("state"));
                    row.put("totalOrderValue", x.get("total_order_value"));
                    return row;
                }).toList());
                var evidence = new LinkedHashMap<String, Object>();
                evidence.put("strategy", "relational");
                evidence.put("orderBy", "total_order_value desc");
                evidence.put("tie", true);
                response.put("evidence", evidence);
                response.put("suggestedRefinements", List.of(
                        "Name the supplier directly (pass supplierName on the next call).",
                        "Give a tiebreak criterion (for example: most recent purchase order, or lowest lead time).",
                        "Narrow to a more specific region than the state."));
                return response;
            }

            supplier = candidates.get(0);
            resolvedBy = "ranking";
        }

        int supplierId = ((Number) supplier.get("supplier_id")).intValue();
        BigDecimal supplierValue = (BigDecimal) supplier.get("total_order_value");
        var runnerUp = candidates.stream()
                .filter(x -> ((Number) x.get("supplier_id")).intValue() != supplierId)
                .toList();
        BigDecimal runnerUpValue = runnerUp.stream()
                .map(x -> (BigDecimal) x.get("total_order_value"))
                .max(Comparator.naturalOrder())
                .orElse(BigDecimal.ZERO);

        var overdueRows = overdueRows(supplierId);
        var today = LocalDate.now();
        var overduePurchaseOrders = overdueRows.stream().map(r -> {
            LocalDate expected = ((java.sql.Date) r.get("expected_date")).toLocalDate();
            Map<String, Object> row = new LinkedHashMap<>();
            row.put("purchaseOrderId", r.get("purchase_order_id"));
            row.put("expectedDate", expected.toString());
            row.put("daysLate", ChronoUnit.DAYS.between(expected, today));
            return row;
        }).toList();

        // Field-level authorization, mirroring step 7 of the walkthrough:
        // NegotiatedCost is denied to every actor except admin, regardless
        // of the fact that the capability call itself was allowed.
        boolean isAdmin = "admin".equalsIgnoreCase(actor);
        List<String> deniedFields = isAdmin ? List.of() : List.of("Supplier.NegotiatedCost");

        var response = new LinkedHashMap<String, Object>();
        response.put("status", "resolved");
        response.put("state", state);
        response.put("resolvedBy", resolvedBy);
        var supplierOut = new LinkedHashMap<String, Object>();
        supplierOut.put("id", supplierId);
        supplierOut.put("name", supplier.get("supplier_name"));
        supplierOut.put("state", supplier.get("state"));
        supplierOut.put("totalOrderValue", supplierValue);
        supplierOut.put("negotiatedCost", isAdmin ? supplier.get("negotiated_cost") : null);
        response.put("supplier", supplierOut);
        var evidence = new LinkedHashMap<String, Object>();
        evidence.put("strategy", "relational");
        evidence.put("orderBy", "total_order_value desc");
        evidence.put("rank", 1);
        evidence.put("marginOverRunnerUp", supplierValue.subtract(runnerUpValue));
        response.put("evidence", evidence);
        var authorization = new LinkedHashMap<String, Object>();
        authorization.put("decision", "allow");
        authorization.put("deniedFields", deniedFields);
        response.put("authorization", authorization);
        response.put("overduePurchaseOrders", overduePurchaseOrders);
        response.put("rowCount", overduePurchaseOrders.size());
        response.put("plan", planFingerprint());
        return response;
    }

    // Retrieval: ranked candidates + provenance. This step alone cannot
    // grant access to anything.
    private List<Map<String, Object>> candidatesForState(String state) throws SQLException {
        try (var stmt = connection.prepareStatement("""
                SELECT supplier_id, supplier_name, state, total_order_value, negotiated_cost
                FROM suppliers
                WHERE state = ?
                ORDER BY total_order_value DESC, supplier_id
                """)) {
            stmt.setString(1, state.toUpperCase(Locale.ROOT));
            return readRows(stmt);
        }
    }

    private List<Map<String, Object>> overdueRows(int supplierId) throws SQLException {
        try (var stmt = connection.prepareStatement("""
                SELECT purchase_order_id, expected_date
                FROM purchase_orders
                WHERE supplier_id = ?
                  AND received_date IS NULL
                  AND expected_date < CURRENT_DATE
                ORDER BY expected_date
                """)) {
            stmt.setInt(1, supplierId);
            return readRows(stmt);
        }
    }

    private record ApproximateMatch(String strategy, List<Map<String, Object>> matches) {
    }

    // Approximate retrieval fallback for a supplierName that didn't match
    // any candidate exactly, tried in increasing order of looseness -
    // mirroring PostgresRetrievalCandidateSource's RetrievalStrategy enum:
    //   1. Fuzzy    - pg_trgm trigram similarity (typos, minor misspellings)
    //   2. FullText - native Postgres full-text search (word order, stemming)
    //   3. Search   - pg_search/BM25 (opt-in, FOUNDGINE_POSTGRES_PGSEARCH=1)
    // GraphSimilarity and Vector don't apply here - there's no relationship
    // or embedding to compare against, just a misspelled or loosely-phrased
    // name - and Relational is the exact match this method is a fallback
    // from. Returns the first strategy that finds anything, or null if none
    // of them do.
    private ApproximateMatch tryApproximateSupplierMatch(String state, String supplierName) throws SQLException {
        var fuzzy = tryFuzzy(state, supplierName);
        if (!fuzzy.isEmpty())
            return new ApproximateMatch("fuzzy", fuzzy);

        var fullText = tryFullText(state, supplierName);
        if (!fullText.isEmpty())
            return new ApproximateMatch("fulltext", fullText);

        if (pgSearchEnabled()) {
            var search = trySearch(state, supplierName);
            if (!search.isEmpty())
                return new ApproximateMatch("search", search);
        }

        return null;
    }

    // pg_trgm's % operator: true when trigram similarity clears the
    // (session-configurable, default 0.3) similarity threshold. Ships in
    // every stock PostgreSQL image's contrib modules and is provisioned by
    // schema.sql (CREATE EXTENSION IF NOT EXISTS pg_trgm).
    private List<Map<String, Object>> tryFuzzy(String state, String supplierName) {
        try (var stmt = connection.prepareStatement("""
                SELECT supplier_id, supplier_name, state, total_order_value, negotiated_cost,
                       similarity(supplier_name, ?) AS score
                FROM suppliers
                WHERE state = ? AND supplier_name % ?
                ORDER BY score DESC
                LIMIT 5
                """)) {
            stmt.setString(1, supplierName);
            stmt.setString(2, state.toUpperCase(Locale.ROOT));
            stmt.setString(3, supplierName);
            return readRows(stmt);
        } catch (SQLException e) {
            // pg_trgm not installed on this instance - degrade to "no
            // fuzzy candidates" instead of failing the whole capability.
            return List.of();
        }
    }

    // Native Postgres full-text search: catches word-order and stemming
    // differences (e.g. "Industrial Metal" vs "Metal Industries") that
    // trigram similarity can miss.
    private List<Map<String, Object>> tryFullText(String state, String supplierName) {
        try (var stmt = connection.prepareStatement("""
                SELECT supplier_id, supplier_name, state, total_order_value, negotiated_cost,
                       ts_rank_cd(to_tsvector('english', supplier_name), websearch_to_tsquery('english', ?)) AS score
                FROM suppliers
                WHERE state = ?
                  AND to_tsvector('english', supplier_name) @@ websearch_to_tsquery('english', ?)
                ORDER BY score DESC
                LIMIT 5
                """)) {
            stmt.setString(1, supplierName);
            stmt.setString(2, state.toUpperCase(Locale.ROOT));
            stmt.setString(3, supplierName);
            return readRows(stmt);
        } catch (SQLException e) {
            return List.of();
        }
    }

    // pg_search/BM25 (ParadeDB), the same provider PostgresRetrievalCandidateSource
    // uses for RetrievalStrategy.Search. Requires the pg_search extension and
    // a BM25 index that this sample does not provision by default, so it's
    // gated by FOUNDGINE_POSTGRES_PGSEARCH=1 (checked by the caller) and
    // degrades to "no candidates" rather than throwing if the extension/
    // operator turns out not to be installed.
    private List<Map<String, Object>> trySearch(String state, String supplierName) {
        try (var stmt = connection.prepareStatement("""
                SELECT supplier_id, supplier_name, state, total_order_value, negotiated_cost,
                       pdb.score(supplier_id) AS score
                FROM suppliers
                WHERE supplier_name ||| ? AND state = ?
                ORDER BY score DESC
                LIMIT 5
                """)) {
            stmt.setString(1, supplierName);
            stmt.setString(2, state.toUpperCase(Locale.ROOT));
            return readRows(stmt);
        } catch (SQLException e) {
            return List.of();
        }
    }

    private static boolean pgSearchEnabled() {
        return "1".equals(System.getenv("FOUNDGINE_POSTGRES_PGSEARCH"));
    }

    private static List<Map<String, Object>> readRows(PreparedStatement stmt) throws SQLException {
        try (var rs = stmt.executeQuery()) {
            ResultSetMetaData meta = rs.getMetaData();
            int columns = meta.getColumnCount();
            var rows = new ArrayList<Map<String, Object>>();
            while (rs.next()) {
                var row = new LinkedHashMap<String, Object>();
                for (int i = 1; i <= columns; i++)
                    row.put(meta.getColumnLabel(i), rs.getObject(i));
                rows.add(row);
            }
            return rows;
        }
    }

    // See the class-level Javadoc: this is a simplified stand-in for the C#
    // version's real SemanticPlan/execution-IR fingerprint, not a port of
    // ExecutionIRCompiler. It is a stable hash of the fixed Supplier read
    // shape (Id, Name, State, TotalOrderValue, NegotiatedCost) this
    // capability always executes.
    private static String planFingerprint() {
        return hash("{\"entity\":\"Supplier\",\"fields\":[\"Id\",\"Name\",\"State\",\"TotalOrderValue\","
                + "\"NegotiatedCost\"]}");
    }

    private static String hash(String s) {
        try {
            var digest = MessageDigest.getInstance("SHA-256").digest(s.getBytes(StandardCharsets.UTF_8));
            return HexFormat.of().formatHex(digest).substring(0, 24);
        } catch (Exception e) {
            throw new IllegalStateException(e);
        }
    }
}
