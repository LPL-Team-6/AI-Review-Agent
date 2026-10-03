using AiReview.Contracts.Input;
using AiReview.Core.Validation;

namespace AiReview.Core;

/// <summary>
/// Produces a proposed review for one case. Implementations only generate output; they never validate,
/// persist, or change case state. ReviewService is the only caller.
/// </summary>
public interface IAiReviewer
{
    /// Identifies the provider in stored reviews and in the UI ("bedrock" | "deterministic").
    string ProviderName { get; }

    // retryErrors carries the previous attempt's failure codes so the retry prompt can include them (RT-2).
    Task<AiReviewResult> ReviewAsync(
        ReviewRequest request,
        CancellationToken ct,
        IReadOnlyCollection<ValidationFailureCode>? retryErrors = null);
}

public static class ReviewProviders
{
    public const string Bedrock = "bedrock";
    public const string Deterministic = "deterministic";
}
