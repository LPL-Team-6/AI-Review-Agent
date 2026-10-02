using System.Text.Json.Serialization;

namespace AiReview.Contracts.Input;

/// <summary>
/// A key-value pair produced by Textract. <see cref="Value"/> is untrusted customer data.
/// </summary>
public sealed record ExtractedField
{
    /// <summary>Formatted as "{document_id}.{name}", for example "DOC-1.name".</summary>
    [JsonPropertyName("field_id")]
    public required string FieldId { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("value")]
    public required string Value { get; init; }

    /// <summary>Textract's confidence, 0–100.</summary>
    [JsonPropertyName("textract_confidence")]
    public required double TextractConfidence { get; init; }
}
