package com.foundgine.core.semantic.planning.mutation;
import com.foundgine.core.abstractions.*; import java.util.List;
/** Common provider-neutral mutation input. */
public interface IMutationIntent { EntityId entity(); MutationKind kind(); List<MutationFieldValue> fields(); }
