using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Metadata;
using Foundgine.Core.Semantic;
using Xunit;

namespace Foundgine.Core.Semantic.Tests;

public sealed class MetadataToSemanticsConfigurationTests
{
    [Fact]
    public void Discovered_metadata_can_be_enriched_without_generated_identity_references()
    {
        var metadata = new MetadataRegistry();
        metadata.Register(new EntityMetadata(
            new EntityId(1), "Customer", [],
            Fields:
            [
                new FieldMetadata(new FieldId(1), "Id", typeof(int),
                    new ColumnReference(new EntityId(1), new ColumnId(1)))
            ],
            PrimaryKey: new ColumnReference(new EntityId(1), new ColumnId(1))));
        metadata.Register(new EntityMetadata(
            new EntityId(2), "CustomerRelationship", [],
            Fields:
            [
                new FieldMetadata(new FieldId(2), "Id", typeof(int),
                    new ColumnReference(new EntityId(2), new ColumnId(2))),
                new FieldMetadata(new FieldId(3), "CustomerId", typeof(int),
                    new ColumnReference(new EntityId(2), new ColumnId(3)))
            ],
            PrimaryKey: new ColumnReference(new EntityId(2), new ColumnId(2))));
        metadata.Register(new EntityMetadata(
            new EntityId(3), "Contract", [],
            Fields:
            [
                new FieldMetadata(new FieldId(4), "Id", typeof(int),
                    new ColumnReference(new EntityId(3), new ColumnId(4)))
            ],
            PrimaryKey: new ColumnReference(new EntityId(3), new ColumnId(4))));
        metadata.Register(new EntityMetadata(
            new EntityId(4), "Transaction", [],
            Fields:
            [
                new FieldMetadata(new FieldId(5), "Id", typeof(int),
                    new ColumnReference(new EntityId(4), new ColumnId(5))),
                new FieldMetadata(new FieldId(6), "ContractId", typeof(int),
                    new ColumnReference(new EntityId(4), new ColumnId(6)))
            ],
            PrimaryKey: new ColumnReference(new EntityId(4), new ColumnId(5))));

        metadata.Register(new RelationshipMetadata(new RelationshipId(10), new EntityId(1), new EntityId(2),
            "relationships", new ColumnReference(new EntityId(1), new ColumnId(1)),
            new ColumnReference(new EntityId(2), new ColumnId(3))));
        metadata.Register(new RelationshipMetadata(new RelationshipId(11), new EntityId(2), new EntityId(3), "contract",
            new ColumnReference(new EntityId(2), new ColumnId(2)),
            new ColumnReference(new EntityId(3), new ColumnId(4)), false));
        metadata.Register(new RelationshipMetadata(new RelationshipId(12), new EntityId(3), new EntityId(4),
            "transactions", new ColumnReference(new EntityId(3), new ColumnId(4)),
            new ColumnReference(new EntityId(4), new ColumnId(6))));

        var model = metadata
            .FromMetadata()
            .Traversal("Customer", "transactions", "relationships", "contract", "transactions")
            .Build();

        var traversal = model.GetTraversal(new EntityId(1), "transactions");
        Assert.Equal(new EntityId(4), traversal.Target);
        Assert.Equal([new RelationshipId(10), new RelationshipId(11), new RelationshipId(12)], traversal.Path);
    }
}