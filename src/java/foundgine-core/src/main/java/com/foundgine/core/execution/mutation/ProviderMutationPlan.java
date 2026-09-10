package com.foundgine.core.execution.mutation;

/**
 * Port of {@code Foundgine.Core.Execution.Mutation.ProviderMutationPlan} (a C#
 * {@code abstract record} with no members).
 *
 * <p>Opaque physical mutation plan produced by a provider compiler. The
 * execution layer does not know the provider's mutation operations.
 *
 * <p>Ported as a non-record {@code abstract class}: a Java record with zero
 * components would carry structural equality shared by every subtype
 * instance (all empty), which is not what an "opaque per-provider plan"
 * marker should have. Subtypes remain free to be records themselves and get
 * their own structural equality from their own components.
 */
public abstract class ProviderMutationPlan {
}
