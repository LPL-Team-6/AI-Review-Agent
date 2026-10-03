using System.Text.Json.Serialization;

namespace AiReview.Contracts.Output;

/// <summary>
/// The model-authored part of a review (spec section 7.1). Bedrock and the deterministic fallback both
/// produce this shape. It holds no confidence and no metadata; the server adds those to the stored record.
/// </summary>
public sealed record ReviewOutput
{
    public const int SummaryMaxLength = 600;
    public const int MaxKeyConcerns = 10;
    public const int ConcernMaxLength = 300;
    public const int MinNextSteps = 1;
    public const int MaxNextSteps = 8;
    public const int NextStepMaxLength = 200;
    public const int DraftCaseNoteMaxLength = 2000;

    /// <summary>Plain-language overview of the case, 1–600 characters.</summary>
    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    /// <summary>0–10 items. Empty only when the request has zero findings (VAL-4).</summary>
    [JsonPropertyName("key_concerns")]
    public required IReadOnlyList<KeyConcern> KeyConcerns { get; init; }

    /// <summary>1–8 actions for the analyst, each 1–200 characters. Never "approve" or "reject".</summary>
    [JsonPropertyName("recommended_next_steps")]
    public required IReadOnlyList<string> RecommendedNextSteps { get; init; }

    /// <summary>Starting draft for the analyst, 1–2000 characters. Never overwritten (OUT-1).</summary>
    [JsonPropertyName("draft_case_note")]
    public required string DraftCaseNote { get; init; }
}
