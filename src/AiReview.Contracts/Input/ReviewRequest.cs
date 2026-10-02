using System.Text.Json.Serialization;

namespace AiReview.Contracts.Input;

/// <summary>
/// Everything the reviewer is allowed to see for one case: extracted fields and rule findings.
/// Never contains raw document text, page images, or full Textract blocks.
/// </summary>
public sealed record ReviewRequest
{
    [JsonPropertyName("case_id")]
    public required string CaseId { get; init; }

    [JsonPropertyName("documents")]
    public required IReadOnlyList<DocumentDto> Documents { get; init; }

    /// <summary>May be empty. The finding IDs here form the allowed citation set (IN-3).</summary>
    [JsonPropertyName("findings")]
    public required IReadOnlyList<RuleFinding> Findings { get; init; }
}
