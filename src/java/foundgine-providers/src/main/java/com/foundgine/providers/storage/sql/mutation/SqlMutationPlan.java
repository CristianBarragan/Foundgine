package com.foundgine.providers.storage.sql.mutation;

import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.execution.mutation.ProviderMutationPlan;
import com.foundgine.providers.storage.sql.query.SqlParameterBinding;
import java.util.*;

public final class SqlMutationPlan extends ProviderMutationPlan {
	private final String commandText;
	private final List<SqlParameterBinding> parameters;
	private final List<MutationReturnBinding> returnedFields;
	private final String fallbackCommandText;

	public SqlMutationPlan(String c, List<SqlParameterBinding> p, List<MutationReturnBinding> r, String f) {
		commandText = Objects.requireNonNull(c);
		parameters = List.copyOf(p);
		returnedFields = List.copyOf(r);
		fallbackCommandText = f;
	}

	public SqlMutationPlan(String c, List<SqlParameterBinding> p, List<MutationReturnBinding> r) {
		this(c, p, r, null);
	}

	public String commandText() {
		return commandText;
	}

	public List<SqlParameterBinding> parameters() {
		return parameters;
	}

	public List<MutationReturnBinding> returnedFields() {
		return returnedFields;
	}

	public String fallbackCommandText() {
		return fallbackCommandText;
	}
}
