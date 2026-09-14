using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.Query;
using Foundgine.Core.Semantic.Security.Execution;
using Xunit;

namespace Foundgine.Security.Tests.Penetration;

/// <summary>Resource-exhaustion attacks are rejected before planning/execution.</summary>
public sealed class ResourceExhaustionPenetrationTests
{
    [Fact]
    public void Huge_selection_depth_is_rejected()
    {
        var selections = new SemanticSelection(new FieldId(1), null, [
            new SemanticSelection(new FieldId(1), null, [
                new SemanticSelection(new FieldId(1), null, [])
            ])
        ]);
        var request = new SemanticRequest(new EntityId(1), [selections]);

        Assert.Throws<InvalidOperationException>(() =>
            SecurityResourceLimitValidator.Validate(request, new SecurityResourceLimits { MaxSelectionDepth = 2 }));
    }

    [Fact]
    public void Huge_filter_tree_is_rejected()
    {
        var filter = new SemanticAndFilter([
            new SemanticFieldFilter(new FieldId(1), SemanticFilterOperator.Eq, 1),
            new SemanticFieldFilter(new FieldId(1), SemanticFilterOperator.Eq, 2),
            new SemanticFieldFilter(new FieldId(1), SemanticFilterOperator.Eq, 3),
            new SemanticFieldFilter(new FieldId(1), SemanticFilterOperator.Eq, 4)
        ]);
        var request = new SemanticRequest(new EntityId(1), [new SemanticSelection(new FieldId(1), null, [])],
            new SemanticQueryOptions(Filter: filter));

        Assert.Throws<InvalidOperationException>(() =>
            SecurityResourceLimitValidator.Validate(request, new SecurityResourceLimits { MaxFilterNodes = 3 }));
    }

    [Fact]
    public void Huge_page_size_is_rejected()
    {
        var request = new SemanticRequest(new EntityId(1), [new SemanticSelection(new FieldId(1), null, [])],
            new SemanticQueryOptions(Limit: 1_000_000));

        Assert.Throws<InvalidOperationException>(() =>
            SecurityResourceLimitValidator.Validate(request, new SecurityResourceLimits { MaxPageSize = 100 }));
    }

    [Fact]
    public void Huge_cursor_is_rejected()
    {
        var request = new SemanticRequest(new EntityId(1), [new SemanticSelection(new FieldId(1), null, [])],
            new SemanticQueryOptions(After: new string('x', 10_001)));

        Assert.Throws<InvalidOperationException>(() =>
            SecurityResourceLimitValidator.Validate(request, new SecurityResourceLimits { MaxCursorLength = 100 }));
    }
}