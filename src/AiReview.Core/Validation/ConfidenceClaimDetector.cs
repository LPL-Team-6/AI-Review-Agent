using System.Text.RegularExpressions;

namespace AiReview.Core.Validation;

/// <summary>
/// Finds confidence claims in model-authored text (VAL-5). Confidence is computed by the server from
/// measured inputs, so any score, level, or rating the model states is rejected.
/// </summary>
public static class ConfidenceClaimDetector
{
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    private static readonly Regex[] Patterns =
    {
        // "Confidence: high", "confidence = 0.9", "confidence - medium"
        new(@"\bconfidence\s*[:=-]", Options),
        // "high confidence", "Low confidence in the TIN match"
        new(@"\b(high|medium|low)\s+confidence\b", Options),
        // "90% confident", "85 % confidence", "95% certain"
        new(@"\b\d{1,3}(\.\d+)?\s*%\s*(confiden|certain)", Options),
    };

    public const string Redaction = "[score removed]";

    // Returns true when the text states a confidence level, score, or rating.
    public static bool ContainsClaim(string text) => Patterns.Any(pattern => pattern.IsMatch(text));

    // Replaces every confidence claim with a fixed marker, for upstream text (such as finding descriptions) that
    // the fallback copies into its output. Repeats until nothing matches, since a removal can join new text.
    public static string Redact(string text)
    {
        for (var pass = 0; pass < 5 && ContainsClaim(text); pass++)
        {
            foreach (var pattern in Patterns)
                text = pattern.Replace(text, Redaction);
        }
        return ContainsClaim(text) ? Redaction : text;
    }
}
