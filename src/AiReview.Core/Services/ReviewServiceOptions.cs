namespace AiReview.Core.Services;

/// <summary>Bound from the "AiReview" configuration section (spec 3.3).</summary>
public sealed class ReviewServiceOptions
{
    public const string SectionName = "AiReview";

    /// <summary>RT-1: one initial attempt plus at most one retry.</summary>
    public const int MaxAllowedAttempts = 2;

    /// <summary>"bedrock" or "deterministic". "deterministic" forces the fallback for demos and offline use.</summary>
    public string Provider { get; set; } = ReviewProviders.Bedrock;

    /// <summary>Bedrock attempts per generation, 1–2.</summary>
    public int MaxAttempts { get; set; } = MaxAllowedAttempts;

    public bool UsesDeterministicOnly =>
        string.Equals(Provider, ReviewProviders.Deterministic, StringComparison.OrdinalIgnoreCase);

    // Returns the problems with these settings (empty when valid); checked at startup.
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (!string.Equals(Provider, ReviewProviders.Bedrock, StringComparison.OrdinalIgnoreCase) && !UsesDeterministicOnly)
            errors.Add($"AiReview:Provider must be \"{ReviewProviders.Bedrock}\" or \"{ReviewProviders.Deterministic}\".");
        if (MaxAttempts is < 1 or > MaxAllowedAttempts)
            errors.Add($"AiReview:MaxAttempts must be between 1 and {MaxAllowedAttempts}.");
        return errors;
    }
}
