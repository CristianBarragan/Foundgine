namespace Foundgine.Core.Semantic.Metadata;

/// <summary>
/// Read-only catalog view used when a semantic model is discovered from
/// structural metadata. It extends the provider contract with model-wide
/// enumeration without coupling semantics to a concrete registry.
/// </summary>
public interface IMetadataCatalog : IMetadataProvider
{
    IEnumerable<EntityMetadata> Entities { get; }

    IEnumerable<RelationshipMetadata> Relationships { get; }
}