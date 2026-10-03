using System.Globalization;
using AiReview.Contracts.Input;
using AiReview.Contracts.Output;
using Microsoft.Extensions.Options;

namespace AiReview.Core.Confidence;

/// <summary>
/// Computes confidence from Textract and rules-engine scores only (spec section 8.1). It never looks at
/// reviewer output, so the same request always gets the same confidence whichever provider wrote the text (CONF-1).
/// </summary>
public sealed class ConfidenceCalculator
{
    private const int Decimals = 4;

    private readonly ConfidenceOptions _options;

    // Creates a calculator using the configured weights and thresholds.
    public ConfidenceCalculator(IOptions<ConfidenceOptions> options)
    {
        _options = options.Value;
    }

    // Returns the score, band, and the breakdown the UI shows under "How this was calculated" (CONF-2).
    public ConfidenceBreakdown Calculate(ReviewRequest request)
    {
        var extraction = Extraction(request);
        var ruleMatch = RuleMatch(request.Findings);

        var weighted = _options.ExtractionWeight * extraction.Mean + _options.RuleMatchWeight * ruleMatch.Mean;
        var cap = extraction.Min + _options.WeakestFieldMargin;
        var score = Round(Math.Clamp(Math.Min(weighted, cap), 0.0, 1.0));

        return new ConfidenceBreakdown
        {
            Score = score,
            Band = Band(score),
            Method = Method(),
            Extraction = extraction,
            RuleMatch = ruleMatch,
            AppliedCap = cap < weighted,
        };
    }

    // E: Textract confidence over fields referenced by any finding; all fields when no finding references one.
    private static ExtractionConfidence Extraction(ReviewRequest request)
    {
        var allFields = request.Documents.SelectMany(doc => doc.Fields).ToList();
        var referenced = request.Findings.SelectMany(f => f.FieldIds).ToHashSet(StringComparer.Ordinal);

        // Request order is kept so the lowest field is chosen the same way every time.
        var used = allFields.Where(field => referenced.Contains(field.FieldId)).ToList();
        if (used.Count == 0)
            used = allFields;

        if (used.Count == 0)
        {
            // Nothing was extracted, so nothing supports the review: E and the cap are both 0.
            return new ExtractionConfidence { Mean = 0, Min = 0, MinFieldId = null, FieldsUsed = 0 };
        }

        var lowest = used.First(field => field.TextractConfidence == used.Min(f => f.TextractConfidence));
        return new ExtractionConfidence
        {
            Mean = Round(used.Average(field => field.TextractConfidence / 100.0)),
            Min = Round(lowest.TextractConfidence / 100.0),
            MinFieldId = lowest.FieldId,
            FieldsUsed = used.Count,
        };
    }

    // R: the mean match score; 1.0 when there are no findings, since there are no matches to doubt.
    private static RuleMatchConfidence RuleMatch(IReadOnlyList<RuleFinding> findings) => new()
    {
        Mean = findings.Count == 0 ? 1.0 : Round(findings.Average(f => f.MatchScore)),
        FindingsUsed = findings.Count,
    };

    private string Band(double score) =>
        score >= _options.HighThreshold ? ConfidenceBands.High
        : score >= _options.MediumThreshold ? ConfidenceBands.Medium
        : ConfidenceBands.Low;

    // Describes the configured formula, e.g. "0.5*extraction + 0.5*rule_match, capped at min_field+0.10".
    private string Method() => string.Create(
        CultureInfo.InvariantCulture,
        $"{_options.ExtractionWeight:0.0###}*extraction + {_options.RuleMatchWeight:0.0###}*rule_match, capped at min_field+{_options.WeakestFieldMargin:0.00##}");

    private static double Round(double value) => Math.Round(value, Decimals, MidpointRounding.AwayFromZero);
}
