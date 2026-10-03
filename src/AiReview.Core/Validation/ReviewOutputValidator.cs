using System.Text.Json;
using System.Text.Json.Serialization;
using AiReview.Contracts.Input;
using AiReview.Contracts.Output;

namespace AiReview.Core.Validation;

/// <summary>
/// The outcome of validating one attempt: the parsed output when valid, otherwise every failure code found.
/// Details describe which rule failed; they never contain output or field text, so they are safe to log (NFR-2).
/// </summary>
public sealed record ReviewValidationResult(
    ReviewOutput? Output,
    IReadOnlyList<ValidationFailureCode> Errors,
    IReadOnlyList<string> Details)
{
    public bool IsValid => Output is not null && Errors.Count == 0;
}

/// <summary>
/// Server-side validation of model-authored output (section 6, VAL-1 to VAL-5). Bedrock and the
/// deterministic fallback go through the same checks (VAL-6).
/// </summary>
public static class ReviewOutputValidator
{
    // VAL-1: unknown properties are rejected; property names are case-sensitive.
    private static readonly JsonSerializerOptions StrictOptions = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    // Parses and checks raw output against the request it answers. Never throws for bad output.
    public static ReviewValidationResult Validate(string? rawOutput, ReviewRequest request)
    {
        if (string.IsNullOrWhiteSpace(rawOutput))
        {
            return Fail(ValidationFailureCode.MalformedJson, "Output is empty.");
        }

        // A single JSON object and nothing else: no prose, code fences, or trailing content.
        try
        {
            using var document = JsonDocument.Parse(rawOutput);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return Fail(ValidationFailureCode.MalformedJson, $"Root is a JSON {document.RootElement.ValueKind}, not an object.");
            }
        }
        catch (JsonException)
        {
            return Fail(ValidationFailureCode.MalformedJson, "Output does not parse as a single JSON object.");
        }

        ReviewOutput? output;
        try
        {
            // Covers missing required properties, wrong types, and extra properties.
            output = JsonSerializer.Deserialize<ReviewOutput>(rawOutput, StrictOptions);
        }
        catch (JsonException ex)
        {
            // ex.Path names the property (for example "$.summary"); the message itself can quote output text.
            return Fail(ValidationFailureCode.SchemaViolation, $"Output does not match the schema at {ex.Path ?? "$"}.");
        }

        if (output is null)
        {
            return Fail(ValidationFailureCode.MalformedJson, "Output is JSON null.");
        }

        var errors = new List<ValidationFailureCode>();
        var details = new List<string>();
        void Add(ValidationFailureCode code, string detail)
        {
            if (!errors.Contains(code))
                errors.Add(code);
            details.Add(detail);
        }

        CheckSchema(output, Add);
        if (errors.Count > 0)
        {
            // Null lists or items would break the checks below.
            return new ReviewValidationResult(null, errors, details);
        }

        CheckCitations(output, request, Add);
        CheckConfidenceClaims(output, Add);

        return new ReviewValidationResult(errors.Count == 0 ? output : null, errors, details);
    }

    // VAL-2: required values present, item counts and string lengths within the section 7.1 limits.
    private static void CheckSchema(ReviewOutput output, Action<ValidationFailureCode, string> add)
    {
        const ValidationFailureCode code = ValidationFailureCode.SchemaViolation;

        CheckText(output.Summary, "summary", ReviewOutput.SummaryMaxLength, add);
        CheckText(output.DraftCaseNote, "draft_case_note", ReviewOutput.DraftCaseNoteMaxLength, add);

        if (output.KeyConcerns is null)
        {
            add(code, "key_concerns is null.");
        }
        else
        {
            if (output.KeyConcerns.Count > ReviewOutput.MaxKeyConcerns)
                add(code, $"key_concerns has {output.KeyConcerns.Count} items; the maximum is {ReviewOutput.MaxKeyConcerns}.");

            for (var i = 0; i < output.KeyConcerns.Count; i++)
            {
                var concern = output.KeyConcerns[i];
                if (concern is null)
                {
                    add(code, $"key_concerns[{i}] is null.");
                    continue;
                }
                CheckText(concern.Concern, $"key_concerns[{i}].concern", ReviewOutput.ConcernMaxLength, add);
                if (concern.CitedFindingIds is null)
                    add(code, $"key_concerns[{i}].cited_finding_ids is null.");
                else if (concern.CitedFindingIds.Any(id => id is null))
                    add(code, $"key_concerns[{i}].cited_finding_ids contains null.");
            }
        }

        if (output.RecommendedNextSteps is null)
        {
            add(code, "recommended_next_steps is null.");
        }
        else
        {
            var count = output.RecommendedNextSteps.Count;
            if (count is < ReviewOutput.MinNextSteps or > ReviewOutput.MaxNextSteps)
                add(code, $"recommended_next_steps has {count} items; it needs {ReviewOutput.MinNextSteps}-{ReviewOutput.MaxNextSteps}.");

            for (var i = 0; i < count; i++)
                CheckText(output.RecommendedNextSteps[i], $"recommended_next_steps[{i}]", ReviewOutput.NextStepMaxLength, add);
        }
    }

    // A required string: not null, not blank, and at most maxLength characters.
    private static void CheckText(string? text, string path, int maxLength, Action<ValidationFailureCode, string> add)
    {
        if (string.IsNullOrWhiteSpace(text))
            add(ValidationFailureCode.SchemaViolation, $"{path} is missing or blank.");
        else if (text.Length > maxLength)
            add(ValidationFailureCode.SchemaViolation, $"{path} has {text.Length} characters; the maximum is {maxLength}.");
    }

    // VAL-3 and VAL-4: concerns cite only the request's findings, and exist exactly when findings exist.
    private static void CheckCitations(ReviewOutput output, ReviewRequest request, Action<ValidationFailureCode, string> add)
    {
        var allowed = request.Findings.Select(f => f.FindingId).ToHashSet(StringComparer.Ordinal);

        if (allowed.Count > 0 && output.KeyConcerns.Count == 0)
            add(ValidationFailureCode.SchemaViolation, "key_concerns is empty but the request has findings.");

        for (var i = 0; i < output.KeyConcerns.Count; i++)
        {
            var cited = output.KeyConcerns[i].CitedFindingIds;
            if (cited.Count == 0)
            {
                add(ValidationFailureCode.UncitedConcern, $"key_concerns[{i}] cites no finding.");
                continue;
            }
            // Only the count is reported: an invented ID is model text.
            var unknown = cited.Count(id => !allowed.Contains(id));
            if (unknown > 0)
                add(ValidationFailureCode.UnknownFindingId, $"key_concerns[{i}] cites {unknown} finding ID(s) not in the request.");
        }
    }

    // VAL-5: no string anywhere in the output may state a confidence.
    private static void CheckConfidenceClaims(ReviewOutput output, Action<ValidationFailureCode, string> add)
    {
        var strings = new List<(string Path, string Text)>
        {
            ("summary", output.Summary),
            ("draft_case_note", output.DraftCaseNote),
        };
        strings.AddRange(output.KeyConcerns.Select((c, i) => ($"key_concerns[{i}].concern", c.Concern)));
        strings.AddRange(output.RecommendedNextSteps.Select((s, i) => ($"recommended_next_steps[{i}]", s)));

        foreach (var (path, text) in strings)
        {
            if (ConfidenceClaimDetector.ContainsClaim(text))
                add(ValidationFailureCode.ModelConfidence, $"{path} contains a confidence claim.");
        }
    }

    private static ReviewValidationResult Fail(ValidationFailureCode code, string detail) =>
        new(null, new[] { code }, new[] { detail });
}
