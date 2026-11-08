using System.Text.Json.Serialization;

namespace Foundgine.EventStore.Tests;

public sealed record TestPayload(string Name, int Amount);

[JsonSerializable(typeof(TestPayload))]
internal partial class TestJsonContext : JsonSerializerContext;
