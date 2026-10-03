using System.Text.Json.Serialization;

namespace AiReview.Contracts.Output;

/// <summary>One concern raised by the reviewer. Must cite at least one finding from the request.</summary>
public sealed record KeyConcern
{
    /// <summary>1–300 characters.</summary>
    [JsonPropertyName("concern")]
    public required string Concern { get; init; }

    /// <summary>At least 1 entry; every ID must exist in the request's findings (VAL-3).</summary>
    [JsonPropertyName("cited_finding_ids")]
    public required IReadOnlyList<string> CitedFindingIds { get; init; }
}
