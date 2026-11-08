using System.Text.Json.Serialization;
using Foundgine.Providers.Gateway.Semantics;
using Foundgine.Providers.Gateway.Providers;
using Foundgine.Providers.Gateway.Http;
using Foundgine.Providers.Gateway.Execution;

namespace Foundgine.Providers.Gateway.Http;

/// <summary>
/// Source-generated JSON metadata for the public gateway contract.
/// Keeping the serialization contract explicit avoids reflection-based serialization
/// in Native AOT deployments.
/// </summary>
[JsonSerializable(typeof(FoundgineGatewayDescription))]
[JsonSerializable(typeof(FoundgineGatewayCapability))]
[JsonSerializable(typeof(FoundgineGatewayEvent))]
[JsonSerializable(typeof(FoundgineGatewayProviderDescriptor))]
[JsonSerializable(typeof(FoundgineGatewayProviderStatus))]
[JsonSerializable(typeof(FoundgineGatewayProviderStatus[]))]
[JsonSerializable(typeof(FoundgineGatewaySemanticDescription))]
[JsonSerializable(typeof(FoundgineGatewayReadRequest))]
[JsonSerializable(typeof(FoundgineGatewayReadResponse))]
[JsonSerializable(typeof(FoundgineGatewayReadRow))]
[JsonSerializable(typeof(FoundgineGatewayReadPageInfo))]
[JsonSerializable(typeof(FoundgineGatewayMutationRequest))]
[JsonSerializable(typeof(FoundgineGatewayMutationDryRunResponse))]
[JsonSerializable(typeof(FoundgineGatewayApprovalResponse))]
[JsonSerializable(typeof(FoundgineGatewayMutationExecutionResponse))]
[JsonSerializable(typeof(FoundgineGatewayExecutionStatus))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    GenerationMode = JsonSourceGenerationMode.Metadata,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
public partial class FoundgineGatewayJsonContext : JsonSerializerContext
{
}
