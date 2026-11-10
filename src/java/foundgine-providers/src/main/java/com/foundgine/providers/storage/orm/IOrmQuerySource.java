package com.foundgine.providers.storage.orm;

import com.foundgine.core.abstractions.FieldId;

import java.util.List;
import java.util.Map;

/** Executes a single-entity read against a concrete persistence technology. */
public interface IOrmQuerySource {
    /** Rows carry exactly the requested {@code select} fields, keyed by field id. */
    List<Map<FieldId, Object>> fetch(OrmFetchRequest request);

    /**
     * Fetches semantic rows plus private aggregate-order values. Legacy/custom sources remain
     * source-compatible, but cannot provide private values unless they override this method.
     */
    default OrmFetchResult fetchWithPrivateOrderValues(OrmFetchRequest request) {
        return OrmFetchResult.semanticOnly(fetch(request));
    }
}
