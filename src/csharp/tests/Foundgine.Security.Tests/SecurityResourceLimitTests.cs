using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.Query;
using Foundgine.Core.Semantic.Security.Execution;
using Xunit;

namespace Foundgine.Security.Tests;

public sealed class SecurityResourceLimitTests
{
    private static readonly EntityId Customer = new(1);
    private static readonly FieldId Id = new(1);
    private static readonly RelationshipId Orders = new(2);

    [Fact]
    public void Non_json_callers_cannot_bypass_selection_complexity_bounds()
    {
        var request = new SemanticRequest(
            Customer,
            [
                new SemanticSelection(
                    Id,
                    null,
                    [new SemanticSelection(Id, null, [])])
            ]);

        var limits = new SecurityResourceLimits { MaxSelectionDepth = 1 };

        var exception =
            Assert.Throws<InvalidOperationException>(() => SecurityResourceLimitValidator.Validate(request, limits));

        Assert.Contains("selection depth", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Page_size_is_bounded_before_planning()
    {
        var request = new SemanticRequest(
            Customer,
            [new SemanticSelection(Id, null, [])],
            new SemanticQueryOptions(Limit: 10));

        var limits = new SecurityResourceLimits { MaxPageSize = 5 };

        var exception =
            Assert.Throws<InvalidOperationException>(() => SecurityResourceLimitValidator.Validate(request, limits));

        Assert.Contains("page size", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Order_path_depth_is_bounded()
    {
        var request = new SemanticRequest(
            Customer,
            [new SemanticSelection(Id, null, [])],
            new SemanticQueryOptions(Order:
            [
                new SemanticOrderTerm(Id, SemanticSortDirection.Asc, [Orders, Orders])
            ]));

        var limits = new SecurityResourceLimits { MaxOrderPathDepth = 1 };

        var exception =
            Assert.Throws<InvalidOperationException>(() => SecurityResourceLimitValidator.Validate(request, limits));

        Assert.Contains("order relationship path", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Filter_node_count_is_bounded()
    {
        var filter = new SemanticAndFilter([
            new SemanticFieldFilter(Id, SemanticFilterOperator.Eq, 1),
            new SemanticFieldFilter(Id, SemanticFilterOperator.Eq, 2),
            new SemanticFieldFilter(Id, SemanticFilterOperator.Eq, 3)
        ]);

        var request = new SemanticRequest(
            Customer,
            [new SemanticSelection(Id, null, [])],
            new SemanticQueryOptions(Filter: filter));

        var limits = new SecurityResourceLimits { MaxFilterNodes = 2 };

        var exception =
            Assert.Throws<InvalidOperationException>(() => SecurityResourceLimitValidator.Validate(request, limits));

        Assert.Contains("filter complexity", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Cursor_length_is_bounded()
    {
        var request = new SemanticRequest(
            Customer,
            [new SemanticSelection(Id, null, [])],
            new SemanticQueryOptions(After: "123456"));

        var limits = new SecurityResourceLimits { MaxCursorLength = 5 };

        var exception =
            Assert.Throws<InvalidOperationException>(() => SecurityResourceLimitValidator.Validate(request, limits));

        Assert.Contains("cursor length", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}