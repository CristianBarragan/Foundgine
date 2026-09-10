package com.foundgine.providers.storage.sql.mutation;
import com.foundgine.core.abstractions.FieldId;
public record MutationReturnBinding(FieldId fieldId,String resultName) {}
