using System.Text.Json.Serialization;

namespace AiReview.Contracts.Input;

public sealed record DocumentDto
{
    [JsonPropertyName("document_id")]
    public required string DocumentId { get; init; }

    /// <summary>For example "W-9".</summary>
    [JsonPropertyName("document_type")]
    public required string DocumentType { get; init; }

    [JsonPropertyName("fields")]
    public required IReadOnlyList<ExtractedField> Fields { get; init; }
}
