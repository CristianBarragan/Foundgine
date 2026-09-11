package com.foundgine.providers.storage.sql;
import java.util.List;
public record SqlPaginationPlan(int first, List<SqlCursorBinding> cursorValues, String after) {
    public SqlPaginationPlan { cursorValues = cursorValues == null ? List.of() : List.copyOf(cursorValues); }
}
