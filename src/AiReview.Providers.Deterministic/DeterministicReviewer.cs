using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AiReview.Contracts.Input;
using AiReview.Contracts.Output;
using AiReview.Core;
using AiReview.Core.Prompting;
using AiReview.Core.Validation;

namespace AiReview.Providers.Deterministic;

/// <summary>
/// Builds the review from templates and the rule findings, with no model call and no network access (NFR-4).
/// The same request always produces byte-identical output.
/// </summary>
public sealed class DeterministicReviewer : IAiReviewer
{
    private const int MaxCaseIdLength = 64;

    // Fixed options so serialization never depends on caller settings.
    private static readonly JsonSerializerOptions OutputJsonOptions = new() { WriteIndented = false };

    public string ProviderName => ReviewProviders.Deterministic;

    // Builds the output synchronously; retryErrors is ignored because templates cannot make the same mistake twice.
    public Task<AiReviewResult> ReviewAsync(
        ReviewRequest request,
        CancellationToken ct,
        IReadOnlyCollection<ValidationFailureCode>? retryErrors = null)
    {
        ct.ThrowIfCancellationRequested();
        var stopwatch = Stopwatch.StartNew();

        var json = JsonSerializer.Serialize(Build(request), OutputJsonOptions);

        return Task.FromResult(AiReviewResult.Success(ProviderName, json, stopwatch.Elapsed));
    }

    // Assembles the four model-authored fields, each kept within the section 7.1 limits.
    public static ReviewOutput Build(ReviewRequest request)
    {
        var caseId = Clean(request.CaseId, MaxCaseIdLength);
        var findings = request.Findings;

        return new ReviewOutput
        {
            Summary = Clean(BuildSummary(caseId, request), ReviewOutput.SummaryMaxLength),
            KeyConcerns = BuildConcerns(findings),
            RecommendedNextSteps = BuildNextSteps(findings),
            DraftCaseNote = Clean(BuildDraftNote(caseId, findings), ReviewOutput.DraftCaseNoteMaxLength),
        };
    }

    // States the finding counts by severity, or that none were raised (IN-4).
    private static string BuildSummary(string caseId, ReviewRequest request)
    {
        var findings = request.Findings;
        if (findings.Count == 0)
        {
            var fieldCount = request.Documents.Sum(doc => doc.Fields.Count);
            return $"No rule findings were raised for case {caseId}. " +
                   $"The case has {request.Documents.Count} document(s) with {fieldCount} extracted field(s). " +
                   "This fallback review was generated from rules, not AI.";
        }

        var high = findings.Count(f => f.Severity == FindingSeverity.High);
        var medium = findings.Count(f => f.Severity == FindingSeverity.Medium);
        var low = findings.Count(f => f.Severity == FindingSeverity.Low);
        return $"The rules engine raised {findings.Count} finding(s) for case {caseId}: " +
               $"{high} high, {medium} medium, and {low} low severity. " +
               "This fallback review was generated from rules, not AI. Verify each finding against the source documents.";
    }

    // One concern per finding in request order; past the 10-item cap, the last concern cites all remaining findings.
    private static IReadOnlyList<KeyConcern> BuildConcerns(IReadOnlyList<RuleFinding> findings)
    {
        var concerns = new List<KeyConcern>();
        var fitsAll = findings.Count <= ReviewOutput.MaxKeyConcerns;
        var individual = fitsAll ? findings.Count : ReviewOutput.MaxKeyConcerns - 1;

        foreach (var finding in findings.Take(individual))
        {
            var description = InputSanitizer.StripControlCharacters(finding.Description);
            concerns.Add(new KeyConcern
            {
                Concern = Clean(ConcernTemplates.Concern(finding, description), ReviewOutput.ConcernMaxLength),
                CitedFindingIds = new[] { finding.FindingId },
            });
        }

        if (!fitsAll)
        {
            var rest = findings.Skip(individual).ToList();
            concerns.Add(new KeyConcern
            {
                Concern = ConcernTemplates.Overflow(rest.Count),
                CitedFindingIds = rest.Select(f => f.FindingId).ToList(),
            });
        }

        return concerns;
    }

    // Distinct steps in finding order, always ending with the case-note step, capped at 8.
    private static IReadOnlyList<string> BuildNextSteps(IReadOnlyList<RuleFinding> findings)
    {
        if (findings.Count == 0)
        {
            return ConcernTemplates.NoFindingSteps;
        }

        var steps = findings
            .Select(ConcernTemplates.NextStep)
            .Distinct(StringComparer.Ordinal)
            .Take(ReviewOutput.MaxNextSteps - 1)
            .Select(step => Clean(step, ReviewOutput.NextStepMaxLength))
            .ToList();
        steps.Add(ConcernTemplates.FinalStep);
        return steps;
    }

    // Lists every finding so the analyst has a starting note; the assessment line is left for the analyst.
    private static string BuildDraftNote(string caseId, IReadOnlyList<RuleFinding> findings)
    {
        var note = new StringBuilder();
        note.Append($"Fallback review for case {caseId}, generated from rules, not AI.\n");

        if (findings.Count == 0)
        {
            note.Append("No rule findings were raised.\n");
        }
        else
        {
            note.Append($"Findings reviewed ({findings.Count}):\n");
            foreach (var finding in findings)
            {
                var description = InputSanitizer.StripControlCharacters(finding.Description);
                note.Append($"- {finding.FindingId} [{ConcernTemplates.SeverityLabel(finding.Severity)}] {finding.RuleId}: {description}\n");
            }
        }

        note.Append("Analyst assessment: [to be completed by the analyst]");
        return note.ToString();
    }

    // Strips control characters (newlines are kept for the case note), removes confidence wording copied from
    // upstream text such as finding descriptions (VAL-5), and caps the length.
    private static string Clean(string text, int maxLength)
    {
        var lines = text.Split('\n').Select(InputSanitizer.StripControlCharacters);
        var cleaned = InputSanitizer.Truncate(ConfidenceClaimDetector.Redact(string.Join('\n', lines)), maxLength);

        // Truncating can complete a phrase such as "high confidence" from "high confidences".
        return ConfidenceClaimDetector.ContainsClaim(cleaned)
            ? InputSanitizer.Truncate(ConfidenceClaimDetector.Redact(cleaned), maxLength)
            : cleaned;
    }
}
