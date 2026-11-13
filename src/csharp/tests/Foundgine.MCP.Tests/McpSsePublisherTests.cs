using Foundgine.Providers.Tools.MCP;
using Xunit;

namespace Foundgine.Providers.Tools.MCP.Tests;

public sealed class McpSsePublisherTests
{
    private static readonly string Id = new('A', 43);

    private sealed class Capture : IFoundgineMcpSseSink
    {
        public readonly List<string> Frames = new();
        public void Write(string frame) => Frames.Add(frame);
        public void Complete() { }
    }

    [Fact]
    public void DeliversOnlyToSessionsThatHaveAStream()
    {
        var registry = new FoundgineMcpSseRegistry();
        var publisher = new FoundgineMcpSsePublisher(registry);
        const string note = "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/tools/list_changed\"}";
        Assert.Null(publisher.Publish(Id, note));        // no buffer: dropped, nothing allocated
        Assert.Equal(0, registry.Count);
        registry.Stream(Id);
        var sink = new Capture();
        FoundgineMcpSseStream.Open(registry.Find(Id)!, null, sink, work => work()).Stream!.Start();
        var published = publisher.Publish(Id, "{ \"jsonrpc\": \"2.0\",\n \"method\": \"notifications/tools/list_changed\" }");
        Assert.NotNull(published);
        Assert.Equal(note, published!.Data);             // re-serialised to one compact line
        Assert.Equal(new[] { "id: 1\ndata: " + note + "\n\n" }, sink.Frames);
    }

    [Fact]
    public void RefusesAnythingButASingleJsonRpcMessage()
    {
        var registry = new FoundgineMcpSseRegistry();
        var publisher = new FoundgineMcpSsePublisher(registry);
        registry.Stream(Id);
        foreach (var bad in new[] { "[]", "{}", "nope", "{\"jsonrpc\":\"1.0\",\"method\":\"x\"}", "{\"jsonrpc\":\"2.0\"}",
                     "{\"jsonrpc\":\"2.0\",\"id\":1}", "[{\"jsonrpc\":\"2.0\",\"method\":\"x\"}]", "\"text\"", "null" })
            Assert.Throws<ArgumentException>(() => publisher.Publish(Id, bad));
        Assert.Equal(0, registry.Find(Id)!.Count);       // nothing was appended
        Assert.NotNull(publisher.Publish(Id, "{\"jsonrpc\":\"2.0\",\"id\":7,\"result\":{}}"));   // a response is allowed
        Assert.Throws<ArgumentNullException>(() => publisher.Publish(Id, null!));
        Assert.Throws<ArgumentNullException>(() => new FoundgineMcpSsePublisher(null!));
    }

    [Fact]
    public void NonAsciiTextStaysReadableAndNewlinesInStringsAreEscaped()
    {
        var registry = new FoundgineMcpSseRegistry();
        var publisher = new FoundgineMcpSsePublisher(registry);
        registry.Stream(Id);
        var e = publisher.Publish(Id, "{\"jsonrpc\":\"2.0\",\"method\":\"m\",\"params\":{\"t\":\"caf\u00e9\\nx\"}}")!;
        Assert.Contains("caf\u00e9", e.Data);
        Assert.DoesNotContain('\n', e.Data);
    }
}
