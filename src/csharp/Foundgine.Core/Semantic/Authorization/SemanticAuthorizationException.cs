namespace Foundgine.Core.Semantic.Authorization;

public sealed class SemanticAuthorizationException(string message) : InvalidOperationException(message);