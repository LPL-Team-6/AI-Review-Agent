using System.Text.Json;
using AiReview.Contracts.Input;

namespace AiReview.Core.Input;

/// <summary>The outcome of reading a ReviewRequest: the request when valid, otherwise the list of problems.</summary>
public sealed record ReviewRequestReadResult(ReviewRequest? Request, IReadOnlyList<string> Errors)
{
    public bool IsValid => Request is not null && Errors.Count == 0;
}

/// <summary>Turns incoming JSON into a ReviewRequest and rejects input that breaks the section 4 contract.</summary>
public static class ReviewRequestReader
{
    public const int MaxDocuments = 50;
    public const int MaxFieldsPerDocument = 200;
    public const int MaxFindings = 100;

    private static readonly JsonSerializerOptions Options = new()
    {
        // Upstream teams may add properties later; ignore them instead of failing the whole case.
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Skip,
    };

    // Parses JSON into a ReviewRequest and validates it; never throws for bad input.
    public static ReviewRequestReadResult Read(string json)
    {
        ReviewRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<ReviewRequest>(json, Options);
        }
        catch (JsonException ex)
        {
            // Covers malformed JSON, wrong types, and missing required properties.
            return new ReviewRequestReadResult(null, new[] { $"Invalid JSON: {ex.Message}" });
        }

        if (request is null)
        {
            return new ReviewRequestReadResult(null, new[] { "Request body is empty." });
        }

        var errors = Validate(request);
        return new ReviewRequestReadResult(errors.Count == 0 ? request : null, errors);
    }

    // Checks an already-built request against the input contract and returns every problem found (empty when valid).
    public static IReadOnlyList<string> Validate(ReviewRequest request)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(request.CaseId))
            errors.Add("case_id is required.");

        // A JSON null passes the 'required' check, so lists are checked explicitly.
        if (request.Documents is null)
            errors.Add("documents must be an array.");
        else if (request.Documents.Count > MaxDocuments)
            errors.Add($"documents has more than {MaxDocuments} items.");

        if (request.Findings is null)
            errors.Add("findings must be an array (it may be empty).");
        else if (request.Findings.Count > MaxFindings)
            errors.Add($"findings has more than {MaxFindings} items.");

        if (errors.Count > 0)
            return errors;

        var fieldIds = ValidateDocuments(request.Documents!, errors);
        ValidateFindings(request.Findings!, fieldIds, errors);
        return errors;
    }

    // Checks every document and field, and returns the set of field IDs so findings can be checked against it.
    private static HashSet<string> ValidateDocuments(IReadOnlyList<DocumentDto> documents, List<string> errors)
    {
        var documentIds = new HashSet<string>(StringComparer.Ordinal);
        var fieldIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var doc in documents)
        {
            if (doc is null)
            {
                errors.Add("documents contains a null entry.");
                continue;
            }
            if (string.IsNullOrWhiteSpace(doc.DocumentId))
                errors.Add("A document is missing document_id.");
            else if (!documentIds.Add(doc.DocumentId))
                errors.Add($"Duplicate document_id '{doc.DocumentId}'.");

            if (doc.Fields is null)
            {
                errors.Add($"Document '{doc.DocumentId}' fields must be an array.");
                continue;
            }
            if (doc.Fields.Count > MaxFieldsPerDocument)
                errors.Add($"Document '{doc.DocumentId}' has more than {MaxFieldsPerDocument} fields.");

            foreach (var field in doc.Fields)
            {
                if (field is null)
                {
                    errors.Add($"Document '{doc.DocumentId}' contains a null field.");
                    continue;
                }
                if (string.IsNullOrWhiteSpace(field.FieldId))
                    errors.Add($"A field in document '{doc.DocumentId}' is missing field_id.");
                else if (!fieldIds.Add(field.FieldId))
                    errors.Add($"Duplicate field_id '{field.FieldId}'.");

                if (field.Value is null)
                    errors.Add($"Field '{field.FieldId}' value must be a string.");
                if (field.TextractConfidence is < 0 or > 100 || double.IsNaN(field.TextractConfidence))
                    errors.Add($"Field '{field.FieldId}' textract_confidence must be between 0 and 100.");
            }
        }

        return fieldIds;
    }

    // Checks every finding: unique ID, known severity, score in range, and field_ids that point at real fields.
    private static void ValidateFindings(IReadOnlyList<RuleFinding> findings, HashSet<string> fieldIds, List<string> errors)
    {
        var findingIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var finding in findings)
        {
            if (finding is null)
            {
                errors.Add("findings contains a null entry.");
                continue;
            }
            if (string.IsNullOrWhiteSpace(finding.FindingId))
                errors.Add("A finding is missing finding_id.");
            else if (!findingIds.Add(finding.FindingId))
                errors.Add($"Duplicate finding_id '{finding.FindingId}'.");

            if (!FindingSeverity.IsValid(finding.Severity))
                errors.Add($"Finding '{finding.FindingId}' severity must be low, medium, or high.");
            if (finding.MatchScore is < 0 or > 1 || double.IsNaN(finding.MatchScore))
                errors.Add($"Finding '{finding.FindingId}' match_score must be between 0.0 and 1.0.");

            if (finding.FieldIds is null)
            {
                errors.Add($"Finding '{finding.FindingId}' field_ids must be an array.");
                continue;
            }
            foreach (var fieldId in finding.FieldIds)
            {
                if (!fieldIds.Contains(fieldId))
                    errors.Add($"Finding '{finding.FindingId}' references unknown field_id '{fieldId}'.");
            }
        }
    }
}
