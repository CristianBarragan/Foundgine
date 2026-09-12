package com.foundgine.core.semantic.intent;

public sealed interface ReadFilter permits ReadFieldFilter, ReadRelationshipFilter, ReadAndFilter, ReadOrFilter {
}
