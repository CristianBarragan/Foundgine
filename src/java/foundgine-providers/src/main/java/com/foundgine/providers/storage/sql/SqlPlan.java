package com.foundgine.providers.storage.sql;

import com.foundgine.core.abstractions.AuthorizationPredicate;
import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.execution.ProviderPlan;
import com.foundgine.core.semantic.query.SemanticSortDirection;
import com.foundgine.providers.storage.sql.query.SqlParameterBinding;
import java.util.List;

/** Physical SQL representation of a provider-independent execution plan. */
public final class SqlPlan extends ProviderPlan {
    private final String commandText;
    private final List<SqlColumnBinding> columns;
    private final List<SqlParameterBinding> parameters;
    private final SqlPaginationPlan pagination;
    private final List<SqlAuthorizationPredicate> authorization;

    public SqlPlan(String commandText, List<SqlColumnBinding> columns, List<SqlParameterBinding> parameters,
                   SqlPaginationPlan pagination, List<SqlAuthorizationPredicate> authorization) {
        super("sql");
        if (commandText == null) throw new NullPointerException("commandText");
        if (columns == null) throw new NullPointerException("columns");
        this.commandText = commandText;
        this.columns = List.copyOf(columns);
        this.parameters = parameters == null ? List.of() : List.copyOf(parameters);
        this.pagination = pagination;
        this.authorization = authorization == null ? List.of() : List.copyOf(authorization);
    }
    public SqlPlan(String commandText, List<SqlColumnBinding> columns) { this(commandText, columns, null, null, null); }
    public String commandText() { return commandText; }
    public List<SqlColumnBinding> columns() { return columns; }
    public List<SqlParameterBinding> parameters() { return parameters; }
    public List<SqlParameterBinding> effectiveParameters() { return parameters; }
    public SqlPaginationPlan pagination() { return pagination; }
    public List<SqlAuthorizationPredicate> authorization() { return authorization; }
}

