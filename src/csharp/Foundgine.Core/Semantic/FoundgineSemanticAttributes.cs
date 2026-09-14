namespace Foundgine.Core.Semantic;

/// <summary>Optional declarative semantic metadata. Configuration is preferred when metadata cannot naturally live with the model.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class SemanticEntityAttribute : Attribute;

/// <summary>Associates a model member with a named semantic authorization policy.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method | AttributeTargets.Property, Inherited = false)]
public sealed class SemanticPolicyAttribute(string name) : Attribute
{
    public string Name { get; } = string.IsNullOrWhiteSpace(name)
        ? throw new ArgumentException("Policy name is required.", nameof(name))
        : name;
}

/// <summary>Marks a model member as explicitly exposed through semantic metadata.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter, Inherited = false)]
public sealed class SemanticFieldAttribute : Attribute;