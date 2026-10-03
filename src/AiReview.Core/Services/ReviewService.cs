using System.Diagnostics;
using AiReview.Contracts.Input;
using AiReview.Contracts.Output;
using AiReview.Core.Confidence;
using AiReview.Core.Hashing;
using AiReview.Core.Input;
using AiReview.Core.Persistence;
using AiReview.Core.Prompting;
using AiReview.Core.Validation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiReview.Core.Services;

public enum AnalystNoteUpdateStatus
{
    Updated,
    NotFound,
    NotLatestVersion,
    TooLong,
}

/// <summary>
/// Owns the generate, validate, retry, and fallback flow (spec section 6). The only caller of IAiReviewer
/// and the only writer of review records (3.2). Generation never fails for the caller: when Bedrock cannot
/// produce valid output, the deterministic fallback is stored instead (RT-3).
/// </summary>
public sealed class ReviewService
{
    public const int MaxAnalystCaseNoteLength = 4000;

    /// <summary>fallback_reason when AiReview:Provider is "deterministic".</summary>
    public const string ConfiguredDeterministicReason = "PROVIDER_DETERMINISTIC";

    /// <summary>fallback_reason when no Bedrock reviewer is registered.</summary>
    public const string BedrockUnavailableReason = "BEDROCK_NOT_REGISTERED";

    private readonly IAiReviewer? _bedrock;
    private readonly IAiReviewer _deterministic;
    private readonly IReviewStore _store;
    private readonly ConfidenceCalculator _confidence;
    private readonly ReviewServiceOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<ReviewService> _logger;

    // bedrock may be null (for example in an offline build); every case then gets the deterministic review.
    public ReviewService(
        IAiReviewer? bedrock,
        IAiReviewer deterministic,
        IReviewStore store,
        ConfidenceCalculator confidence,
        IOptions<ReviewServiceOptions> options,
        TimeProvider time,
        ILogger<ReviewService> logger)
    {
        _bedrock = bedrock;
        _deterministic = deterministic;
        _store = store;
        _confidence = confidence;
        _options = options.Value;
        _time = time;
        _logger = logger;
    }

    // Generates, validates, and stores the next review version for the case. Throws only for an invalid
    // request, a cancelled call, or a deterministic reviewer bug.
    public async Task<ReviewRecord> GenerateAsync(ReviewRequest request, CancellationToken ct)
    {
        var problems = ReviewRequestReader.Validate(request);
        if (problems.Count > 0)
        {
            throw new ArgumentException($"The review request breaks the input contract: {string.Join(" ", problems)}", nameof(request));
        }

        var draft = new Draft(
            CanonicalJsonHasher.Hash(request),
            _confidence.Calculate(request));

        string fallbackReason;
        var attempts = 0;

        if (_options.UsesDeterministicOnly)
        {
            fallbackReason = ConfiguredDeterministicReason;
        }
        else if (_bedrock is null)
        {
            fallbackReason = BedrockUnavailableReason;
        }
        else
        {
            var maxAttempts = Math.Clamp(_options.MaxAttempts, 1, ReviewServiceOptions.MaxAllowedAttempts);
            var allFailures = new List<ValidationFailureCode>();
            IReadOnlyList<ValidationFailureCode>? retryErrors = null;

            while (attempts < maxAttempts)
            {
                attempts++;
                var (result, validation) = await AttemptAsync(_bedrock, request, attempts, retryErrors, ct);
                if (validation.IsValid)
                {
                    return await StoreAsync(request, draft, result, validation.Output!, attempts, fallbackReason: null, ct);
                }
                // RT-2: the retry gets the error codes, never the previous output.
                retryErrors = validation.Errors;
                allFailures.AddRange(validation.Errors);
            }

            fallbackReason = string.Join(", ", allFailures.Distinct().Select(code => code.ToCode()));
        }

        var (fallback, fallbackValidation) = await AttemptAsync(_deterministic, request, attempts + 1, retryErrors: null, ct);
        if (!fallbackValidation.IsValid)
        {
            // VAL-6 makes this unreachable unless the templates have a bug; storing invalid output would be worse.
            _logger.LogCritical("Deterministic review for case {CaseId} failed validation: {Details}",
                request.CaseId, string.Join(" ", fallbackValidation.Details));
            throw new InvalidOperationException("The deterministic reviewer produced output that fails validation.");
        }

        return await StoreAsync(request, draft, fallback, fallbackValidation.Output!, attempts, fallbackReason, ct);
    }

    // Saves the analyst's note on the latest version. The model-authored output, including draft_case_note, is untouched (OUT-1).
    public async Task<(AnalystNoteUpdateStatus Status, ReviewRecord? Record)> SetAnalystCaseNoteAsync(
        string caseId, int version, string? note, CancellationToken ct)
    {
        if (note is { Length: > MaxAnalystCaseNoteLength })
        {
            return (AnalystNoteUpdateStatus.TooLong, null);
        }

        var existing = await _store.GetVersionAsync(caseId, version, ct);
        if (existing is null)
        {
            return (AnalystNoteUpdateStatus.NotFound, null);
        }

        var updated = await _store.SetAnalystCaseNoteAsync(caseId, version, note, ct);
        return updated is null
            ? (AnalystNoteUpdateStatus.NotLatestVersion, null)
            : (AnalystNoteUpdateStatus.Updated, updated);
    }

    // Runs one reviewer call and validates it. Any exception other than the caller's cancellation is a transport failure.
    private async Task<(AiReviewResult Result, ReviewValidationResult Validation)> AttemptAsync(
        IAiReviewer reviewer,
        ReviewRequest request,
        int attempt,
        IReadOnlyList<ValidationFailureCode>? retryErrors,
        CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        AiReviewResult result;
        try
        {
            result = await reviewer.ReviewAsync(request, ct, retryErrors);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Only the exception type is kept: messages can echo request content.
            result = AiReviewResult.TransportFailure(reviewer.ProviderName, $"Unexpected {ex.GetType().Name}.", stopwatch.Elapsed);
        }

        var validation = result.IsTransportFailure
            ? new ReviewValidationResult(null, new[] { ValidationFailureCode.TransportError }, new[] { result.TransportError ?? "Transport failure." })
            : ReviewOutputValidator.Validate(result.RawOutput, request);

        LogAttempt(request.CaseId, attempt, result, validation);
        return (result, validation);
    }

    // RT-4: case ID, attempt, provider, latency, tokens, and failure codes. Never field values or output text (NFR-2).
    private void LogAttempt(string caseId, int attempt, AiReviewResult result, ReviewValidationResult validation)
    {
        if (validation.IsValid)
        {
            _logger.LogInformation(
                "Review attempt {Attempt} for case {CaseId}: provider {Provider}, {LatencyMs} ms, {InputTokens} input / {OutputTokens} output tokens, valid",
                attempt, caseId, result.Provider, (long)result.Latency.TotalMilliseconds, result.InputTokens, result.OutputTokens);
            return;
        }

        _logger.LogWarning(
            "Review attempt {Attempt} for case {CaseId}: provider {Provider}, {LatencyMs} ms, {InputTokens} input / {OutputTokens} output tokens, failed {FailureCodes}: {Details}",
            attempt, caseId, result.Provider, (long)result.Latency.TotalMilliseconds, result.InputTokens, result.OutputTokens,
            string.Join(",", validation.Errors.Select(code => code.ToCode())), string.Join(" ", validation.Details));
    }

    private async Task<ReviewRecord> StoreAsync(
        ReviewRequest request,
        Draft draft,
        AiReviewResult result,
        ReviewOutput output,
        int attempts,
        string? fallbackReason,
        CancellationToken ct)
    {
        var isFallback = result.Provider == ReviewProviders.Deterministic;
        var record = new ReviewRecord
        {
            ReviewId = Guid.NewGuid(),
            CaseId = request.CaseId,
            Version = 0, // assigned by the store
            Provider = result.Provider,
            IsFallback = isFallback,
            FallbackReason = isFallback ? fallbackReason : null,
            ModelId = isFallback ? null : result.ModelId,
            PromptVersion = PromptBuilder.PromptVersion,
            InputHash = draft.InputHash,
            ContentHash = CanonicalJsonHasher.Hash(output),
            CreatedAt = _time.GetUtcNow(),
            Attempts = attempts,
            Output = output,
            Confidence = draft.Confidence,
            AnalystCaseNote = null,
        };

        var stored = await _store.AppendAsync(record, ct);
        _logger.LogInformation(
            "Stored review v{Version} for case {CaseId}: provider {Provider}, fallback {IsFallback} ({FallbackReason}), {Attempts} Bedrock attempt(s)",
            stored.Version, stored.CaseId, stored.Provider, stored.IsFallback, stored.FallbackReason, stored.Attempts);
        return stored;
    }

    // Values that depend only on the request, so they are the same whichever provider writes the text (CONF-1).
    private sealed record Draft(string InputHash, ConfidenceBreakdown Confidence);
}
