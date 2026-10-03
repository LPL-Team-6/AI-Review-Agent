using System.Text.Json.Serialization;

namespace AiReview.Contracts.Approval;

/// <summary>
/// An analyst's sign-off request. The client sends back the review ID and content hash it displayed, so an
/// approval can only land on the exact version the analyst saw (APR-3).
/// </summary>
public sealed record ApprovalRequest
{
    [JsonPropertyName("review_id")]
    public required Guid ReviewId { get; init; }

    [JsonPropertyName("content_hash")]
    public required string ContentHash { get; init; }

    /// <summary>The approving analyst. Taken from the request until the API has authentication.</summary>
    [JsonPropertyName("approved_by")]
    public required string ApprovedBy { get; init; }
}
