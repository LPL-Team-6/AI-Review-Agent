using AiReview.Contracts.Output;

namespace AiReview.Core.Persistence;

/// <summary>Versioned review records. ReviewService is the only writer (section 3.2).</summary>
public interface IReviewStore
{
    // Stores the record as the case's next version (previous + 1, starting at 1) and returns it with that version set.
    // The Version on the record passed in is ignored.
    Task<ReviewRecord> AppendAsync(ReviewRecord record, CancellationToken ct);

    Task<ReviewRecord?> GetLatestAsync(string caseId, CancellationToken ct);

    Task<ReviewRecord?> GetVersionAsync(string caseId, int version, CancellationToken ct);

    // Returns every version of the case, oldest first.
    Task<IReadOnlyList<ReviewRecord>> ListAsync(string caseId, CancellationToken ct);

    // Replaces only analyst_case_note, and only on the latest version (older versions are read-only, APR-1).
    // Returns the updated record, or null when that version does not exist or is not the latest.
    Task<ReviewRecord?> SetAnalystCaseNoteAsync(string caseId, int version, string? note, CancellationToken ct);
}
