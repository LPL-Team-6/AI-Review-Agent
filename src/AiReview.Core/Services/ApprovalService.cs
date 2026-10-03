using AiReview.Contracts.Approval;
using AiReview.Core.Persistence;
using AiReview.Core.Prompting;

namespace AiReview.Core.Services;

public enum ApprovalOutcome
{
    Approved,
    /// <summary>The case has no review to approve.</summary>
    NotFound,
    /// <summary>The review ID or content hash is not the latest version's (APR-3); HTTP 409.</summary>
    Conflict,
    Invalid,
}

public sealed record ApprovalResult(ApprovalOutcome Outcome, ApprovalRecord? Approval, string? Error);

/// <summary>
/// Binds an analyst's sign-off to the exact review version they saw (section 9). An approval records the
/// review only: this service has no access to case state, and any case decision is a separate action (APR-5, APR-6).
/// </summary>
public sealed class ApprovalService
{
    public const int MaxApprovedByLength = 200;

    private readonly IReviewStore _reviews;
    private readonly IApprovalStore _approvals;
    private readonly TimeProvider _time;

    public ApprovalService(IReviewStore reviews, IApprovalStore approvals, TimeProvider time)
    {
        _reviews = reviews;
        _approvals = approvals;
        _time = time;
    }

    // Records the approval only if review_id and content_hash both match the case's latest version.
    public async Task<ApprovalResult> ApproveAsync(string caseId, ApprovalRequest request, CancellationToken ct)
    {
        var approvedBy = InputSanitizer.StripControlCharacters(request.ApprovedBy ?? "").Trim();
        if (approvedBy.Length == 0 || approvedBy.Length > MaxApprovedByLength)
        {
            return new ApprovalResult(ApprovalOutcome.Invalid, null, $"approved_by must be 1-{MaxApprovedByLength} characters.");
        }
        if (string.IsNullOrWhiteSpace(request.ContentHash))
        {
            return new ApprovalResult(ApprovalOutcome.Invalid, null, "content_hash is required.");
        }

        var latest = await _reviews.GetLatestAsync(caseId, ct);
        if (latest is null)
        {
            return new ApprovalResult(ApprovalOutcome.NotFound, null, $"Case '{caseId}' has no review.");
        }

        if (request.ReviewId != latest.ReviewId || !string.Equals(request.ContentHash, latest.ContentHash, StringComparison.Ordinal))
        {
            return new ApprovalResult(
                ApprovalOutcome.Conflict,
                null,
                $"The review shown is not the latest version. Reload v{latest.Version} and review it before approving.");
        }

        var approval = new ApprovalRecord
        {
            ApprovalId = Guid.NewGuid(),
            CaseId = caseId,
            ReviewId = latest.ReviewId,
            Version = latest.Version,
            ContentHash = latest.ContentHash,
            ApprovedBy = approvedBy,
            ApprovedAt = _time.GetUtcNow(),
        };
        await _approvals.AddAsync(approval, ct);
        return new ApprovalResult(ApprovalOutcome.Approved, approval, null);
    }

    // Returns whether the latest version is approved, and which approvals are stale (APR-4). Null when the case has no review.
    public async Task<ApprovalStatus?> GetStatusAsync(string caseId, CancellationToken ct)
    {
        var latest = await _reviews.GetLatestAsync(caseId, ct);
        if (latest is null)
        {
            return null;
        }

        var approvals = await _approvals.ListAsync(caseId, ct);
        bool IsCurrent(ApprovalRecord a) =>
            a.ReviewId == latest.ReviewId && string.Equals(a.ContentHash, latest.ContentHash, StringComparison.Ordinal);

        var current = approvals.LastOrDefault(IsCurrent);
        return new ApprovalStatus
        {
            CaseId = caseId,
            LatestVersion = latest.Version,
            LatestReviewId = latest.ReviewId,
            IsApproved = current is not null,
            ApprovalNeeded = current is null ? $"Approval needed for v{latest.Version}" : null,
            CurrentApproval = current,
            StaleApprovals = approvals.Where(a => !IsCurrent(a)).ToList(),
        };
    }
}
