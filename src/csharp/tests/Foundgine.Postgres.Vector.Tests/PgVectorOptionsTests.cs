using Foundgine.Providers.Storage.PostgresVector;
using Xunit;

namespace Foundgine.Providers.Storage.PostgresVector.Tests;

public sealed class PgVectorOptionsTests
{
    [Fact]
    public void Defaults_match_the_documented_lexicon_table_shape()
    {
        var options = new PgVectorOptions();

        Assert.Equal("foundgine_semantic_lexicon", options.TableName);
        Assert.Equal(1536, options.Dimensions);
        Assert.Equal(PgVectorDistance.Cosine, options.Distance);
        Assert.Equal("public", options.Schema);
    }

    [Fact]
    public void QualifiedTableName_double_quotes_schema_and_table()
    {
        var options = new PgVectorOptions(TableName: "lexicon", Schema: "fg_vector");

        Assert.Equal("\"fg_vector\".\"lexicon\"", options.QualifiedTableName);
    }

    [Fact]
    public void QualifiedTableName_reflects_a_non_default_schema_and_table_together()
    {
        var options = new PgVectorOptions(TableName: "custom_lexicon", Schema: "tenant_a");

        Assert.Equal("\"tenant_a\".\"custom_lexicon\"", options.QualifiedTableName);
    }

    [Theory]
    [InlineData(PgVectorDistance.Cosine)]
    [InlineData(PgVectorDistance.L2)]
    [InlineData(PgVectorDistance.InnerProduct)]
    public void Distance_can_be_set_independently_of_other_options(PgVectorDistance distance)
    {
        var options = new PgVectorOptions(Distance: distance);

        Assert.Equal(distance, options.Distance);
        // Changing distance must never perturb the derived qualified name.
        Assert.Equal("\"public\".\"foundgine_semantic_lexicon\"", options.QualifiedTableName);
    }
}
