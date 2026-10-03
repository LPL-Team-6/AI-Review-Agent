using AiReview.Contracts.Approval;

namespace AiReview.Core.Persistence;

/// <summary>Approval records. Append-only: stale approvals are kept for audit (APR-4).</summary>
public interface IApprovalStore
{
    Task AddAsync(ApprovalRecord approval, CancellationToken ct);

    // Returns every approval for the case, oldest first.
    Task<IReadOnlyList<ApprovalRecord>> ListAsync(string caseId, CancellationToken ct);
}
