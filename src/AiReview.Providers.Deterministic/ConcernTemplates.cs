using AiReview.Contracts.Input;

namespace AiReview.Providers.Deterministic;

/// <summary>
/// Fixed wording for the fallback review. Only finding metadata is used; field values are never copied in.
/// None of the text may contain confidence wording, or the validator rejects it (VAL-5).
/// </summary>
internal static class ConcernTemplates
{
    public const string FinalStep = "Record the outcome of your review in the analyst case note.";

    public static readonly IReadOnlyList<string> NoFindingSteps = new[]
    {
        "Spot-check the extracted fields against the source documents.",
        FinalStep,
    };

    // Next steps for rules whose remedy is known. Add entries here as the rules-engine team finalizes rule IDs (D-1).
    private static readonly Dictionary<string, string> RuleSteps = new(StringComparer.Ordinal)
    {
        ["TIN_NAME_MISMATCH"] = "Confirm the name and TIN with the customer and request a corrected W-9 if they do not match.",
    };

    // Returns the concern sentence for one finding, e.g. "High severity (TIN_NAME_MISMATCH): Name on W-9 ... Fields: DOC-1.name."
    public static string Concern(RuleFinding finding, string description)
    {
        var fields = finding.FieldIds.Count > 0 ? $" Fields: {string.Join(", ", finding.FieldIds)}." : "";
        return $"{SeverityLabel(finding.Severity)} severity ({finding.RuleId}): {description}{fields}";
    }

    // Returns the concern used when more findings exist than key_concerns can hold.
    public static string Overflow(int count) =>
        $"{count} additional findings were raised. See the draft case note for the full list.";

    // Returns the next step for a finding: the rule's own step if known, otherwise one based on severity.
    public static string NextStep(RuleFinding finding)
    {
        if (RuleSteps.TryGetValue(finding.RuleId, out var step))
        {
            return step;
        }
        return finding.Severity == FindingSeverity.High
            ? $"Verify the fields cited in {finding.FindingId} against the source document and request a corrected document if needed."
            : $"Check the fields cited in {finding.FindingId} against the source document.";
    }

    // Capitalizes a known severity; anything else is shown as "Unknown".
    public static string SeverityLabel(string severity) => severity switch
    {
        FindingSeverity.High => "High",
        FindingSeverity.Medium => "Medium",
        FindingSeverity.Low => "Low",
        _ => "Unknown",
    };
}
