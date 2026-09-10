package com.foundgine.providers.storage.sql;
import com.foundgine.core.abstractions.AuthorizationPredicate;
public record SqlAuthorizationPredicate(int nodeId, AuthorizationPredicate predicate) {}
