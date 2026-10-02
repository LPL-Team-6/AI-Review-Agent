using System.Text.Json.Serialization;

namespace AiReview.Contracts.Input;

/// <summary>Output of the rules engine. This component consumes findings; it never creates them.</summary>
public sealed record RuleFinding
{
    [JsonPropertyName("finding_id")]
    public required string FindingId { get; init; }

    [JsonPropertyName("rule_id")]
    public required string RuleId { get; init; }

    /// <summary>One of <see cref="FindingSeverity"/>: "low", "medium", or "high".</summary>
    [JsonPropertyName("severity")]
    public required string Severity { get; init; }

    /// <summary>0.0–1.0 from the rules engine.</summary>
    [JsonPropertyName("match_score")]
    public required double MatchScore { get; init; }

    [JsonPropertyName("description")]
    public required string Description { get; init; }

    [JsonPropertyName("field_ids")]
    public required IReadOnlyList<string> FieldIds { get; init; }
}

public static class FindingSeverity
{
    public const string Low = "low";
    public const string Medium = "medium";
    public const string High = "high";

    public static bool IsValid(string severity) => severity is Low or Medium or High;
}
