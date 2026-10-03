using System.Text.Json.Serialization;

namespace AiReview.Contracts.Output;

/// <summary>
/// One stored review of a case (spec section 7.2). Built by the server around the validated output.
/// Everything except <see cref="AnalystCaseNote"/> is immutable once stored (OUT-1).
/// </summary>
public sealed record ReviewRecord
{
    [JsonPropertyName("review_id")]
    public required Guid ReviewId { get; init; }

    [JsonPropertyName("case_id")]
    public required string CaseId { get; init; }

    /// <summary>Starts at 1 and increases by 1 each time the case's review is regenerated. Assigned by the store (APR-1).</summary>
    [JsonPropertyName("version")]
    public required int Version { get; init; }

    /// <summary>"bedrock" or "deterministic". The UI labels deterministic reviews as fallbacks (OUT-2).</summary>
    [JsonPropertyName("provider")]
    public required string Provider { get; init; }

    [JsonPropertyName("is_fallback")]
    public required bool IsFallback { get; init; }

    /// <summary>Why the fallback was used, for example "MALFORMED_JSON, TRANSPORT_ERROR". Null when not a fallback.</summary>
    [JsonPropertyName("fallback_reason")]
    public string? FallbackReason { get; init; }

    /// <summary>The Bedrock model or inference profile ID; null for deterministic reviews.</summary>
    [JsonPropertyName("model_id")]
    public string? ModelId { get; init; }

    [JsonPropertyName("prompt_version")]
    public required string PromptVersion { get; init; }

    /// <summary>SHA-256 of the canonical ReviewRequest.</summary>
    [JsonPropertyName("input_hash")]
    public required string InputHash { get; init; }

    /// <summary>SHA-256 of the canonical <see cref="Output"/>. An approval must present this value (APR-3).</summary>
    [JsonPropertyName("content_hash")]
    public required string ContentHash { get; init; }

    [JsonPropertyName("created_at")]
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>Bedrock attempts made (0–2). Zero when the deterministic provider was configured.</summary>
    [JsonPropertyName("attempts")]
    public required int Attempts { get; init; }

    [JsonPropertyName("output")]
    public required ReviewOutput Output { get; init; }

    [JsonPropertyName("confidence")]
    public required ConfidenceBreakdown Confidence { get; init; }

    /// <summary>The analyst's own note. Kept separate so draft_case_note is never overwritten (OUT-1).</summary>
    [JsonPropertyName("analyst_case_note")]
    public string? AnalystCaseNote { get; init; }
}
