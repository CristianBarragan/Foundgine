using Foundgine.Providers.Storage.Sql.Retrieval;
using Foundgine.Core.Semantic.Resolution;
using Xunit;

namespace Foundgine.Providers.Storage.Sql.Tests;

public sealed class PostgresRetrievalOptionsTests
{
    [Fact]
    public void Search_is_a_distinct_semantic_capability()
    {
        Assert.True(SemanticRetrievalPlanner.RequiresApproximateRetrieval(RetrievalStrategy.Search));
    }

    [Fact]
    public void PgSearch_is_opt_in()
    {
        var options = new PostgresRetrievalOptions();
        Assert.False(options.EnablePgSearch);
        Assert.True(options.EnablePgTrgm);
        Assert.True(options.EnableFullText);
    }
}
