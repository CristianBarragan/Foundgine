using Foundgine.Core.Abstractions;

namespace Foundgine.Core.Semantic.Resolution;

/// <summary>Projects a frozen semantic contract into searchable lexical documents.</summary>
public static class SemanticLexiconProjection
{
    public static IReadOnlyList<SemanticLexiconEntry> Build(SemanticContractSnapshot contract)
    {
        ArgumentNullException.ThrowIfNull(contract);

        var entries = new List<SemanticLexiconEntry>();

        foreach (var entity in contract.Entities)
        {
            entries.Add(new SemanticLexiconEntry(
                entity.Name,
                SemanticLexicalCandidateKind.Entity,
                entity.Name,
                EntityId: entity.Id,
                Aliases: entity.EffectiveAliases.Select(x => x.Name).ToArray(),
                Description: $"Semantic entity {entity.Name}."));

            entries.Add(new SemanticLexiconEntry(
                entity.Name,
                SemanticLexicalCandidateKind.Node,
                entity.Name,
                EntityId: entity.Id,
                Aliases: entity.EffectiveAliases.Select(x => x.Name).ToArray(),
                Description: $"Semantic graph node for {entity.Name}."));

            foreach (var field in entity.Fields)
            {
                entries.Add(new SemanticLexiconEntry(
                    field.Name,
                    SemanticLexicalCandidateKind.Field,
                    $"{entity.Name} {field.Name}",
                    EntityId: entity.Id,
                    FieldId: field.Id,
                    Aliases: field.EffectiveAliases.Select(x => x.Name).ToArray(),
                    Description: $"Field {entity.Name}.{field.Name}."));
            }

            foreach (var relationship in entity.Relationships)
            {
                entries.Add(new SemanticLexiconEntry(
                    relationship.Name,
                    SemanticLexicalCandidateKind.Relationship,
                    $"{entity.Name} {relationship.Name} {contract.Get(relationship.Target).Name}",
                    RelationshipId: relationship.Id,
                    SourceEntityId: entity.Id,
                    TargetEntityId: relationship.Target,
                    Aliases: relationship.EffectiveAliases.Select(x => x.Name).ToArray(),
                    Description: $"Relationship from {entity.Name} to {contract.Get(relationship.Target).Name}."));
            }
        }

        foreach (var traversal in contract.Traversals)
        {
            entries.Add(new SemanticLexiconEntry(
                traversal.Name,
                SemanticLexicalCandidateKind.Traversal,
                $"{contract.Get(traversal.Source).Name} {traversal.Name} {contract.Get(traversal.Target).Name}",
                SourceEntityId: traversal.Source,
                TargetEntityId: traversal.Target,
                Description:
                $"Logical traversal from {contract.Get(traversal.Source).Name} to {contract.Get(traversal.Target).Name}."));
        }

        return entries;
    }
}