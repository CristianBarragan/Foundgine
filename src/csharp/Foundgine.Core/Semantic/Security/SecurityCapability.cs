namespace Foundgine.Core.Semantic.Security;

/// <summary>What may be done. Kept distinct from restrictions and execution invariants.</summary>
public sealed record SecurityCapability(string Id, string Operation, string ResourceScope);

/// <summary>Under what restrictions a capability may be exercised.</summary>
public sealed record SecurityConstraint(string Name, string Value);

/// <summary>What must remain true throughout planning and execution.</summary>
public sealed record SecurityInvariantRequirement(string Id);
