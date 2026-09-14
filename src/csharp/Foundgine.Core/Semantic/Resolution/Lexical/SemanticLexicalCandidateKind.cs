using System.Text.Json.Serialization;

namespace Foundgine.Core.Semantic.Resolution;

/// <summary>Semantic kinds that lexical retrieval may propose for a token.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SemanticLexicalCandidateKind : byte
{
    Entity,
    Node,
    Relationship,
    Traversal,
    Field,
    Value,
    Operation
}