using System.Net.Http.Json;
using System.Text.Json;
using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Resolution;

namespace Foundgine.Providers.Storage.Elasticsearch;

/// <summary>
/// Elasticsearch implementation of Foundgine's provider-neutral lexical
/// candidate source. Elasticsearch supplies ranked hypotheses; Foundgine still
/// validates semantic topology and produces the final interpretation.
/// </summary>
public sealed class ElasticsearchSemanticLexicalCandidateSource : ISemanticLexicalCandidateSource
{
    private readonly HttpClient _httpClient;
    private readonly string _index;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public ElasticsearchSemanticLexicalCandidateSource(
        HttpClient httpClient,
        string index = "foundgine-semantic-lexicon")
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        if (string.IsNullOrWhiteSpace(index))
            throw new ArgumentException("Elasticsearch index cannot be empty.", nameof(index));
        _index = index;
    }

    public IReadOnlyList<SemanticLexicalCandidate> Retrieve(SemanticLexicalRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return RetrieveAsync(request, CancellationToken.None).GetAwaiter().GetResult();
    }

    public IReadOnlyList<SemanticLexicalCandidate> Retrieve(
        SemanticLexicalRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return RetrieveAsync(request, cancellationToken).GetAwaiter().GetResult();
    }

    public async Task<IReadOnlyList<SemanticLexicalCandidate>> RetrieveAsync(
        SemanticLexicalRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var kindNames = request.EffectiveKinds.Select(x => x.ToString()).ToArray();
        var body = new
        {
            size = request.Limit,
            query = new
            {
                @bool = new
                {
                    must = new object[]
                    {
                        new
                        {
                            multi_match = new
                            {
                                query = request.Token,
                                fields = new[] { "canonicalName^4", "aliases^3", "searchText^2", "description" },
                                fuzziness = "AUTO"
                            }
                        }
                    },
                    filter = new object[]
                    {
                        new { terms = new { kind = kindNames } }
                    }
                }
            }
        };

        if (request.ContextEntity is not null)
        {
            // Context is deliberately a retrieval hint, not a semantic
            // authorization check. The core resolver performs authoritative
            // graph compatibility validation.
            body = new
            {
                size = request.Limit,
                query = new
                {
                    @bool = new
                    {
                        must = new object[]
                        {
                            new
                            {
                                multi_match = new
                                {
                                    query = request.Token,
                                    fields = new[] { "canonicalName^4", "aliases^3", "searchText^2", "description" },
                                    fuzziness = "AUTO"
                                }
                            }
                        },
                        filter = new object[]
                        {
                            new { terms = new { kind = kindNames } },
                            new
                            {
                                @bool = new
                                {
                                    should = new object[]
                                    {
                                        new { term = new { entityId = request.ContextEntity.Value.Value } },
                                        new { term = new { sourceEntityId = request.ContextEntity.Value.Value } },
                                        new { term = new { targetEntityId = request.ContextEntity.Value.Value } }
                                    },
                                    minimum_should_match = 1
                                }
                            }
                        }
                    }
                }
            };
        }

        using var response = await _httpClient.PostAsJsonAsync($"/{Uri.EscapeDataString(_index)}/_search", body,
            _jsonOptions, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (!document.RootElement.TryGetProperty("hits", out var hits) ||
            !hits.TryGetProperty("hits", out var hitArray))
            return [];

        var results = new List<SemanticLexicalCandidate>();
        foreach (var hit in hitArray.EnumerateArray())
        {
            if (!hit.TryGetProperty("_source", out var source)) continue;
            var candidate = ReadCandidate(request.Token, hit, source);
            if (candidate is not null) results.Add(candidate);
        }

        return results
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.CanonicalName, StringComparer.OrdinalIgnoreCase)
            .Take(request.Limit)
            .ToArray();
    }

    private static SemanticLexicalCandidate? ReadCandidate(
        string token,
        JsonElement hit,
        JsonElement source)
    {
        if (!source.TryGetProperty("kind", out var kindProperty) ||
            !Enum.TryParse<SemanticLexicalCandidateKind>(kindProperty.GetString(), true, out var kind) ||
            !source.TryGetProperty("canonicalName", out var canonicalProperty))
            return null;

        var score = hit.TryGetProperty("_score", out var scoreProperty) &&
                    scoreProperty.ValueKind == JsonValueKind.Number
            ? scoreProperty.GetDouble()
            : 0d;

        return new SemanticLexicalCandidate(
            token,
            kind,
            canonicalProperty.GetString() ?? string.Empty,
            score,
            ReadEntityId(source, "entityId"),
            ReadRelationshipId(source, "relationshipId"),
            ReadFieldId(source, "fieldId"),
            ReadEntityId(source, "sourceEntityId"),
            ReadEntityId(source, "targetEntityId"),
            source.TryGetProperty("value", out var value) ? value.GetString() : null,
            [
                new ResolutionEvidence(
                    $"Elasticsearch lexical match for '{token}'.",
                    CandidateEvidenceKind.Bm25,
                    score)
            ]);
    }

    private static EntityId? ReadEntityId(JsonElement source, string name) =>
        source.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? new EntityId(value.GetUInt64())
            : null;

    private static RelationshipId? ReadRelationshipId(JsonElement source, string name) =>
        source.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? new RelationshipId(value.GetUInt64())
            : null;

    private static FieldId? ReadFieldId(JsonElement source, string name) =>
        source.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? new FieldId(value.GetUInt64())
            : null;
}