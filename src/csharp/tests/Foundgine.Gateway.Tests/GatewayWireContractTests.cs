using System.Text.Json;
using Foundgine.Providers.Gateway.Http;
using Foundgine.Core.Semantic.Intent;
using Xunit;

namespace Foundgine.Gateway.Tests;

public sealed class GatewayWireContractTests
{
    [Fact]
    public void ReadRequest_UsesSourceGeneratedJsonMetadata()
    {
        const string json = """{"rootEntity":"Customer","selections":[{"field":"Id"},{"relationship":"Orders","children":[{"field":"Total"}]}],"filter":{"kind":"field","field":"Status","operator":"Eq","value":"Active"},"limit":25}""";
        var request = JsonSerializer.Deserialize(json, FoundgineGatewayJsonContext.Default.FoundgineGatewayReadRequest);
        Assert.NotNull(request);
        Assert.Equal("Customer", request.RootEntity);
        Assert.Equal(2, request.Selections!.Count);
        Assert.Equal("Orders", request.Selections[1].Relationship);
        Assert.Equal("Active", request.Filter!.Value.GetString());
    }

    [Fact]
    public void ReadResponse_UsesSourceGeneratedJsonMetadata()
    {
        var response = new FoundgineGatewayReadResponse(
            [new FoundgineGatewayReadRow(new Dictionary<string, JsonElement>
            {
                ["Id"] = JsonDocument.Parse("42").RootElement.Clone()
            })],
            new FoundgineGatewayReadPageInfo(null, "next", true, false));
        var json = JsonSerializer.Serialize(response, FoundgineGatewayJsonContext.Default.FoundgineGatewayReadResponse);
        Assert.Contains("\"hasNextPage\":true", json, StringComparison.Ordinal);
        Assert.Contains("\"Id\":42", json, StringComparison.Ordinal);
    }

    [Fact]
    public void ExecutionStatus_UsesSourceGeneratedJsonMetadata()
    {
        var status = new Foundgine.Providers.Gateway.Execution.FoundgineGatewayExecutionStatus(
            "0123456789abcdef0123456789abcdef", "succeeded", "read",
            DateTimeOffset.Parse("2026-11-08T10:00:00Z"), DateTimeOffset.Parse("2026-11-08T10:00:01Z"), "succeeded");
        var json = JsonSerializer.Serialize(status, FoundgineGatewayJsonContext.Default.FoundgineGatewayExecutionStatus);
        Assert.Contains("\"executionId\"", json, StringComparison.Ordinal);
        Assert.Contains("\"status\":\"succeeded\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadRequest_MapsToCanonicalReadIntent()
    {
        var request = new FoundgineGatewayReadRequest
        {
            RootEntity = "Customer",
            Selections = [new FoundgineGatewayReadSelection { Field = "Id" }],
            Limit = 20
        };
        var intent = FoundgineGatewayReadRequestMapper.ToIntent(request, new FoundgineGatewayHttpOptions());
        Assert.IsType<ReadIntent>(intent);
        Assert.Equal("Customer", intent.RootEntity);
        Assert.Equal("Id", intent.Selections[0].Field);
        Assert.Equal(20, intent.Limit);
        Assert.Null(intent.Security);
    }

    [Fact]
    public void ReadRequest_RejectsAmbiguousSelection()
    {
        var request = new FoundgineGatewayReadRequest
        {
            RootEntity = "Customer",
            Selections = [new FoundgineGatewayReadSelection { Field = "Id", Relationship = "Orders" }]
        };
        Assert.Throws<ArgumentException>(() => FoundgineGatewayReadRequestMapper.ToIntent(request, new FoundgineGatewayHttpOptions()));
    }

    [Fact]
    public void ReadRequest_UnknownPropertiesAreRejected()
    {
        const string json = """{"rootEntity":"Customer","selections":[{"field":"Id"}],"security":{"tenant":"attacker"}}""";
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(
            json, FoundgineGatewayJsonContext.Default.FoundgineGatewayReadRequest));
    }
    [Fact]
    public void MutationRequest_UsesSourceGeneratedJsonMetadata()
    {
        const string json = """{"operations":[{"entityId":42,"kind":"Create","fields":{"1001":"Ada","1002":true},"returnFields":[1001]}]}""";
        var request = JsonSerializer.Deserialize(json, FoundgineGatewayJsonContext.Default.FoundgineGatewayMutationRequest);
        Assert.NotNull(request);
        var graph = FoundgineGatewayMutationRequestMapper.ToGraph(request, new FoundgineGatewayHttpOptions());
        Assert.Single(graph.Operations);
        Assert.Equal(42UL, graph.Operations[0].Entity.Value);
        Assert.Equal(Foundgine.Core.Semantic.Mutation.SemanticMutationKind.Create, graph.Operations[0].Kind);
        Assert.Equal(2, graph.Operations[0].Fields.Count);
        Assert.Single(graph.Operations[0].ReturnFields);
    }

    [Fact]
    public void MutationRequest_RejectsUpdateWithoutFilter()
    {
        var request = new FoundgineGatewayMutationRequest
        {
            Operations = [new FoundgineGatewayMutationOperation { EntityId = 42, Kind = "Update" }]
        };
        Assert.Throws<ArgumentException>(() => FoundgineGatewayMutationRequestMapper.ToGraph(request, new FoundgineGatewayHttpOptions()));
    }

    [Fact]
    public void MutationRequest_RejectsUnknownSecurityProperties()
    {
        const string json = """{"operations":[{"entityId":42,"kind":"Create"}],"security":{"tenant":"attacker"}}""";
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(json,
            FoundgineGatewayJsonContext.Default.FoundgineGatewayMutationRequest));
    }

    [Fact]
    public void MutationRequest_RejectsUpsertWithoutConflictFields()
    {
        var request = new FoundgineGatewayMutationRequest
        {
            Operations = [new FoundgineGatewayMutationOperation { EntityId = 42, Kind = "Upsert" }]
        };
        Assert.Throws<ArgumentException>(() => FoundgineGatewayMutationRequestMapper.ToGraph(request, new FoundgineGatewayHttpOptions()));
    }

    [Fact]
    public void MutationDryRunResponse_UsesSourceGeneratedJsonMetadata()
    {
        var response = new FoundgineGatewayMutationDryRunResponse("plan", [], ["CreateEntity"]);
        var json = JsonSerializer.Serialize(response, FoundgineGatewayJsonContext.Default.FoundgineGatewayMutationDryRunResponse);
        Assert.Contains("\"planFingerprint\":\"plan\"", json, StringComparison.Ordinal);
        Assert.Contains("CreateEntity", json, StringComparison.Ordinal);
    }

    [Fact]
    public void MutationRequest_RejectsUnsupportedFilterKinds()
    {
        var request = new FoundgineGatewayMutationRequest
        {
            Operations = [new FoundgineGatewayMutationOperation
            {
                EntityId = 42, Kind = "Delete",
                Filter = new FoundgineGatewayMutationFilter { Kind = "relationship" }
            }]
        };
        Assert.Throws<ArgumentException>(() => FoundgineGatewayMutationRequestMapper.ToGraph(request, new FoundgineGatewayHttpOptions()));
    }

}
