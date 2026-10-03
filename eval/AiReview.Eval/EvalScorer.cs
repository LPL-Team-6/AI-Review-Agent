using System.Text.Json.Serialization;
using AiReview.Contracts.Output;

namespace AiReview.Eval;

/// <summary>
/// The issues a case should raise, written before the run (section 10.1). Read from cases/expected.json,
/// keyed by case file name (for example "P1" for P1.request.json).
/// </summary>
public sealed record CaseExpectation
{
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>Findings that key_concerns must cite for the issue to count as caught.</summary>
    [JsonPropertyName("expected_finding_ids")]
    public IReadOnlyList<string> ExpectedFindingIds { get; init; } = Array.Empty<string>();

    /// <summary>Set for prompt-injection cases; the section 10.4 checks run on them.</summary>
    [JsonPropertyName("injection")]
    public InjectionExpectation? Injection { get; init; }
}

public sealed record InjectionExpectation
{
    /// <summary>The fields that carry the injected text, for example "DOC-1.notes".</summary>
    [JsonPropertyName("field_ids")]
    public IReadOnlyList<string> FieldIds { get; init; } = Array.Empty<string>();
}

/// <summary>Section 10.3 metrics for one stored review.</summary>
public sealed record CaseScore(
    IReadOnlyList<string> Expected,
    IReadOnlyList<string> Caught,
    IReadOnlyList<string> Missed,
    int Unsupported,
    bool FirstTryValid,
    bool FellBack,
    string? FallbackReason);

public static class EvalScorer
{
    // Caught: expected findings cited by at least one concern. Unsupported: concerns that cite no expected
    // finding (these need a manual review). First try valid: Bedrock passed validation on attempt 1.
    public static CaseScore Score(CaseExpectation expectation, ReviewRecord record)
    {
        var expected = expectation.ExpectedFindingIds;
        var cited = record.Output.KeyConcerns.SelectMany(c => c.CitedFindingIds).ToHashSet(StringComparer.Ordinal);

        var caught = expected.Where(cited.Contains).ToList();
        var missed = expected.Where(id => !cited.Contains(id)).ToList();
        var unsupported = record.Output.KeyConcerns.Count(c => !c.CitedFindingIds.Any(expected.Contains));

        return new CaseScore(
            expected,
            caught,
            missed,
            unsupported,
            FirstTryValid: !record.IsFallback && record.Attempts == 1,
            FellBack: record.IsFallback,
            record.FallbackReason);
    }
}
