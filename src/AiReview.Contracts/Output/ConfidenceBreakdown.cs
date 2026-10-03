using System.Text.Json.Serialization;

namespace AiReview.Contracts.Output;

/// <summary>
/// Server-computed confidence and how it was reached (spec section 8.2). Never produced by the model.
/// </summary>
public sealed record ConfidenceBreakdown
{
    /// <summary>0.0–1.0, rounded to 4 decimal places.</summary>
    [JsonPropertyName("score")]
    public required double Score { get; init; }

    /// <summary>"High", "Medium", or "Low"; see <see cref="ConfidenceBands"/>.</summary>
    [JsonPropertyName("band")]
    public required string Band { get; init; }

    /// <summary>The formula in words, for example "0.5*extraction + 0.5*rule_match, capped at min_field+0.10".</summary>
    [JsonPropertyName("method")]
    public required string Method { get; init; }

    [JsonPropertyName("extraction")]
    public required ExtractionConfidence Extraction { get; init; }

    [JsonPropertyName("rule_match")]
    public required RuleMatchConfidence RuleMatch { get; init; }

    /// <summary>True when the weakest-field cap lowered the score.</summary>
    [JsonPropertyName("applied_cap")]
    public required bool AppliedCap { get; init; }
}

/// <summary>Textract confidence (0.0–1.0) over the fields the findings reference, or all fields when none are referenced.</summary>
public sealed record ExtractionConfidence
{
    [JsonPropertyName("mean")]
    public required double Mean { get; init; }

    [JsonPropertyName("min")]
    public required double Min { get; init; }

    /// <summary>The lowest-confidence field, named in the UI breakdown (CONF-2). Null when the case has no fields.</summary>
    [JsonPropertyName("min_field_id")]
    public string? MinFieldId { get; init; }

    [JsonPropertyName("fields_used")]
    public required int FieldsUsed { get; init; }
}

/// <summary>The rules engine's match scores. Mean is 1.0 when there are no findings.</summary>
public sealed record RuleMatchConfidence
{
    [JsonPropertyName("mean")]
    public required double Mean { get; init; }

    [JsonPropertyName("findings_used")]
    public required int FindingsUsed { get; init; }
}

public static class ConfidenceBands
{
    public const string High = "High";
    public const string Medium = "Medium";
    public const string Low = "Low";
}
