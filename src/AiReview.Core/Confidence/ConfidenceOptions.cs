namespace AiReview.Core.Confidence;

/// <summary>Bound from the "AiReview:Confidence" configuration section. Defaults are the section 8.1 formula.</summary>
public sealed class ConfidenceOptions
{
    public const string SectionName = "AiReview:Confidence";

    public double ExtractionWeight { get; set; } = 0.5;

    public double RuleMatchWeight { get; set; } = 0.5;

    /// <summary>The score may not exceed the weakest field's confidence plus this margin.</summary>
    public double WeakestFieldMargin { get; set; } = 0.10;

    public double HighThreshold { get; set; } = 0.85;

    public double MediumThreshold { get; set; } = 0.65;

    // Returns the problems with these settings (empty when valid); checked at startup.
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (ExtractionWeight < 0 || RuleMatchWeight < 0)
            errors.Add("Confidence weights must not be negative.");
        if (Math.Abs(ExtractionWeight + RuleMatchWeight - 1.0) > 1e-9)
            errors.Add("ExtractionWeight and RuleMatchWeight must add up to 1.");
        if (WeakestFieldMargin < 0)
            errors.Add("WeakestFieldMargin must not be negative.");
        if (MediumThreshold is < 0 or > 1 || HighThreshold is < 0 or > 1 || MediumThreshold > HighThreshold)
            errors.Add("Thresholds must be between 0 and 1, with MediumThreshold <= HighThreshold.");
        return errors;
    }
}
