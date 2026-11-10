package com.foundgine.providers.storage.orm;

import com.foundgine.core.abstractions.FieldId;

/** Resolved join keys of one relationship, seen from the parent entity. */
public record OrmLink(FieldId parentField, FieldId childField) {}
