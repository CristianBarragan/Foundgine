using Foundgine.Generated;
using Foundgine.Core.Semantic.Metadata;
using Foundgine.SupplyChain.Advanced.Semantics;
using Xunit;

namespace Foundgine.SupplyChain.Advanced.Tests;

public sealed class MetadataProducerBoundaryTests
{
    [Fact]
    public void Generated_metadata_exposes_structural_metadata_through_the_metadata_catalog_boundary()
    {
        IMetadataCatalog catalog = GeneratedMetadata.Build();

        Assert.Equal(17, catalog.Entities.Count());
        Assert.Equal(15, catalog.Relationships.Count());
        Assert.Contains(catalog.Entities, entity => entity.Name == "Product");
        Assert.Contains(catalog.Entities, entity => entity.Name == "ComplianceIncident");

        foreach (var relationship in catalog.Relationships)
        {
            var source = catalog.GetEntity(relationship.Source);
            var target = catalog.GetEntity(relationship.Target);
            Assert.Contains(source.Columns, column => column.Id == relationship.SourceKey.ColumnId);
            Assert.Contains(target.Columns, column => column.Id == relationship.TargetKey.ColumnId);
        }
    }

    [Fact]
    public void Semantic_configuration_consumes_generated_metadata_not_a_second_structural_graph()
    {
        var model = SupplyChainSemanticModel.Build();

        Assert.Equal(GeneratedMetadata.Build().Entities.Count(), model.Entities.Count);
        Assert.Equal(GeneratedMetadata.Build().Relationships.Count(),
            model.Entities.SelectMany(x => x.Relationships).Count());
    }
}