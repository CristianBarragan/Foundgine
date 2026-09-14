using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.Resolution;

namespace Foundgine.Providers.Storage.Elasticsearch;

/// <summary>Indexes the derived semantic lexicon projection into Elasticsearch.</summary>
public sealed class SemanticLexiconIndexClient
{
    private readonly HttpClient _httpClient;
    private readonly string _index;
    private readonly JsonSerializerOptions _options = new(JsonSerializerDefaults.Web);

    public SemanticLexiconIndexClient(HttpClient httpClient, string index = "foundgine-semantic-lexicon")
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        if (string.IsNullOrWhiteSpace(index))
            throw new ArgumentException("Elasticsearch index cannot be empty.", nameof(index));
        _index = index;
    }

    public async Task EnsureIndexAsync(CancellationToken cancellationToken = default)
    {
        using var head = await _httpClient.SendAsync(
            new HttpRequestMessage(HttpMethod.Head, $"/{Uri.EscapeDataString(_index)}"),
            cancellationToken);

        if (head.IsSuccessStatusCode)
            return;

        if (head.StatusCode != System.Net.HttpStatusCode.NotFound)
        {
            head.EnsureSuccessStatusCode();
        }

        var mapping = new
        {
            mappings = new
            {
                properties = new
                {
                    canonicalName = new { type = "text" },
                    kind = new { type = "keyword" },
                    searchText = new { type = "text" },
                    aliases = new { type = "text" },
                    description = new { type = "text" },
                    entityId = new { type = "long" },
                    relationshipId = new { type = "long" },
                    fieldId = new { type = "long" },
                    sourceEntityId = new { type = "long" },
                    targetEntityId = new { type = "long" },
                    value = new { type = "text" }
                }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Put, $"/{Uri.EscapeDataString(_index)}")
        {
            Content = JsonContent.Create(mapping, options: _options)
        };

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task IndexContractAsync(
        SemanticContractSnapshot contract,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(contract);

        await EnsureIndexAsync(cancellationToken);

        var entries = SemanticLexiconProjection.Build(contract);
        var payload = new StringBuilder();
        foreach (var entry in entries)
        {
            payload.AppendLine("{\"index\":{}}");
            payload.AppendLine(JsonSerializer.Serialize(entry, _options));
        }

        using var content = new StringContent(payload.ToString(), Encoding.UTF8, "application/x-ndjson");
        using var response = await _httpClient.PostAsync(
            $"/{Uri.EscapeDataString(_index)}/_bulk",
            content,
            cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task IndexEntryAsync(
        SemanticLexiconEntry entry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        using var response = await _httpClient.PostAsJsonAsync(
            $"/{Uri.EscapeDataString(_index)}/_doc",
            entry,
            _options,
            cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}