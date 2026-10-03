using System.Text.Json.Serialization;

namespace AiReview.Contracts.Approval;

/// <summary>
/// An analyst's sign-off on one review version (APR-2). Records the review, not a case decision: any case
/// state change is a separate action owned by the case workflow (APR-6). Kept for audit even after it goes stale.
/// </summary>
public sealed record ApprovalRecord
{
    [JsonPropertyName("approval_id")]
    public required Guid ApprovalId { get; init; }

    [JsonPropertyName("case_id")]
    public required string CaseId { get; init; }

    [JsonPropertyName("review_id")]
    public required Guid ReviewId { get; init; }

    [JsonPropertyName("version")]
    public required int Version { get; init; }

    [JsonPropertyName("content_hash")]
    public required string ContentHash { get; init; }

    [JsonPropertyName("approved_by")]
    public required string ApprovedBy { get; init; }

    [JsonPropertyName("approved_at")]
    public required DateTimeOffset ApprovedAt { get; init; }
}

/// <summary>
/// Where a case's approval stands. Only an approval of the latest version counts; approvals of earlier
/// versions are stale (APR-4).
/// </summary>
public sealed record ApprovalStatus
{
    [JsonPropertyName("case_id")]
    public required string CaseId { get; init; }

    [JsonPropertyName("latest_version")]
    public required int LatestVersion { get; init; }

    [JsonPropertyName("latest_review_id")]
    public required Guid LatestReviewId { get; init; }

    [JsonPropertyName("is_approved")]
    public required bool IsApproved { get; init; }

    /// <summary>For example "Approval needed for v3"; null when the latest version is approved.</summary>
    [JsonPropertyName("approval_needed")]
    public string? ApprovalNeeded { get; init; }

    /// <summary>The approval of the latest version, if any.</summary>
    [JsonPropertyName("current_approval")]
    public ApprovalRecord? CurrentApproval { get; init; }

    /// <summary>Approvals of earlier versions. Kept for audit; they do not count.</summary>
    [JsonPropertyName("stale_approvals")]
    public required IReadOnlyList<ApprovalRecord> StaleApprovals { get; init; }
}
