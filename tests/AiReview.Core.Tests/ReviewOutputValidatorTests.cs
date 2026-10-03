using System.Text.Json;
using System.Text.Json.Nodes;
using AiReview.Contracts.Input;
using AiReview.Core.Validation;
using AiReview.Providers.Deterministic;

namespace AiReview.Core.Tests;

public class ReviewOutputValidatorTests
{
    [Fact]
    public void Validate_ValidOutput_Passes()
    {
        var result = ReviewOutputValidator.Validate(ValidJson(), TestData.Request(TestData.Finding("F-001")));

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
        Assert.Equal("F-001", result.Output!.KeyConcerns.Single().CitedFindingIds.Single());
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("Here is the review: {\"summary\": \"x\"}")]
    public void Validate_NotASingleObject_IsMalformedJson(string raw)
    {
        AssertOnly(ValidationFailureCode.MalformedJson, ReviewOutputValidator.Validate(raw, TestData.Request()));
    }

    [Fact]
    public void Validate_TrailingTextAfterObject_IsMalformedJson()
    {
        var raw = ValidJson() + "\nLet me know if you need anything else.";

        AssertOnly(ValidationFailureCode.MalformedJson, ReviewOutputValidator.Validate(raw, TestData.Request(TestData.Finding("F-001"))));
    }

    [Fact]
    public void Validate_CodeFence_IsMalformedJson()
    {
        var raw = "```json\n" + ValidJson() + "\n```";

        AssertOnly(ValidationFailureCode.MalformedJson, ReviewOutputValidator.Validate(raw, TestData.Request(TestData.Finding("F-001"))));
    }

    public static TheoryData<string> SchemaViolations => new()
    {
        // Missing required property.
        Mutate(o => o.Remove("draft_case_note")),
        // Unknown property (VAL-1).
        Mutate(o => o["confidence"] = 0.9),
        // Wrong property-name casing is an unknown property.
        Mutate(o => { o["Summary"] = o["summary"]!.DeepClone(); o.Remove("summary"); }),
        // Wrong type.
        Mutate(o => o["summary"] = 42),
        Mutate(o => o["key_concerns"] = "none"),
        // Null and blank required strings.
        Mutate(o => o["summary"] = null),
        Mutate(o => o["summary"] = "   "),
        // Length limits.
        Mutate(o => o["summary"] = new string('x', 601)),
        Mutate(o => o["draft_case_note"] = new string('x', 2001)),
        Mutate(o => o["key_concerns"]![0]!["concern"] = new string('x', 301)),
        Mutate(o => o["recommended_next_steps"]![0] = new string('x', 201)),
        // Item counts.
        Mutate(o => o["recommended_next_steps"] = new JsonArray()),
        Mutate(o => o["recommended_next_steps"] = new JsonArray(Enumerable.Range(1, 9).Select(i => (JsonNode)$"Step {i}").ToArray())),
        Mutate(o => o["key_concerns"] = new JsonArray(Enumerable.Range(1, 11).Select(_ => (JsonNode)Concern("F-001")).ToArray())),
        // Unknown property inside a concern.
        Mutate(o => o["key_concerns"]![0]!["severity"] = "high"),
        // Null citation entry.
        Mutate(o => o["key_concerns"]![0]!["cited_finding_ids"] = new JsonArray((JsonNode?)null)),
        // VAL-4: the request has findings, so key_concerns cannot be empty.
        Mutate(o => o["key_concerns"] = new JsonArray()),
    };

    [Theory]
    [MemberData(nameof(SchemaViolations))]
    public void Validate_SchemaViolation_IsRejected(string raw)
    {
        AssertOnly(ValidationFailureCode.SchemaViolation, ReviewOutputValidator.Validate(raw, TestData.Request(TestData.Finding("F-001"))));
    }

    [Fact]
    public void Validate_CitesUnknownFinding_IsUnknownFindingId()
    {
        var raw = Mutate(o => o["key_concerns"] = new JsonArray(Concern("F-001", "F-999")));

        AssertOnly(ValidationFailureCode.UnknownFindingId, ReviewOutputValidator.Validate(raw, TestData.Request(TestData.Finding("F-001"))));
    }

    [Fact]
    public void Validate_FindingIdsAreCaseSensitive()
    {
        var raw = Mutate(o => o["key_concerns"] = new JsonArray(Concern("f-001")));

        AssertOnly(ValidationFailureCode.UnknownFindingId, ReviewOutputValidator.Validate(raw, TestData.Request(TestData.Finding("F-001"))));
    }

    [Fact]
    public void Validate_ConcernWithNoCitations_IsUncitedConcern()
    {
        var raw = Mutate(o => o["key_concerns"] = new JsonArray(Concern()));

        AssertOnly(ValidationFailureCode.UncitedConcern, ReviewOutputValidator.Validate(raw, TestData.Request(TestData.Finding("F-001"))));
    }

    [Fact]
    public void Validate_NoFindingsButAConcern_IsRejected()
    {
        // With no findings there is nothing to cite, so any concern cites an unknown ID (VAL-4).
        var result = ReviewOutputValidator.Validate(ValidJson(), TestData.Request());

        AssertOnly(ValidationFailureCode.UnknownFindingId, result);
    }

    [Fact]
    public void Validate_NoFindingsAndNoConcerns_Passes()
    {
        var raw = Mutate(o => o["key_concerns"] = new JsonArray());

        Assert.True(ReviewOutputValidator.Validate(raw, TestData.Request()).IsValid);
    }

    [Theory]
    [InlineData("summary", "Confidence: high. The name does not match.")]
    [InlineData("summary", "The TIN check is a high confidence match.")]
    [InlineData("draft_case_note", "Overall confidence = 0.92")]
    [InlineData("draft_case_note", "We are 90% confident the W-9 is wrong.")]
    [InlineData("concern", "LOW CONFIDENCE in the extracted name.")]
    [InlineData("step", "Confidence - medium; request a corrected W-9.")]
    public void Validate_ConfidenceClaim_IsModelConfidence(string target, string text)
    {
        var raw = Mutate(o =>
        {
            switch (target)
            {
                case "concern": o["key_concerns"]![0]!["concern"] = text; break;
                case "step": o["recommended_next_steps"]![0] = text; break;
                default: o[target] = text; break;
            }
        });

        AssertOnly(ValidationFailureCode.ModelConfidence, ReviewOutputValidator.Validate(raw, TestData.Request(TestData.Finding("F-001"))));
    }

    [Theory]
    [InlineData("The analyst should confirm the name with the customer.")]
    [InlineData("Textract read the name field clearly.")]
    [InlineData("Confidence is calculated separately by the system.")]
    public void Validate_TextWithoutAConfidenceClaim_Passes(string summary)
    {
        var raw = Mutate(o => o["summary"] = summary);

        Assert.True(ReviewOutputValidator.Validate(raw, TestData.Request(TestData.Finding("F-001"))).IsValid);
    }

    [Fact]
    public void Validate_SeveralProblems_ReportsEveryCode()
    {
        var raw = Mutate(o =>
        {
            o["summary"] = "High confidence that the name is wrong.";
            o["key_concerns"] = new JsonArray(Concern("F-404"), Concern());
        });

        var result = ReviewOutputValidator.Validate(raw, TestData.Request(TestData.Finding("F-001")));

        Assert.False(result.IsValid);
        Assert.Equal(
            new[] { ValidationFailureCode.UnknownFindingId, ValidationFailureCode.UncitedConcern, ValidationFailureCode.ModelConfidence }.OrderBy(c => c),
            result.Errors.OrderBy(c => c));
    }

    [Fact]
    public void Validate_Details_NeverContainOutputText()
    {
        var secret = "SSN 123-45-6789";
        var raw = Mutate(o =>
        {
            o["summary"] = $"High confidence: {secret}";
            o["key_concerns"] = new JsonArray(Concern(secret));
            o["recommended_next_steps"] = new JsonArray(secret + new string('x', 300));
        });

        var result = ReviewOutputValidator.Validate(raw, TestData.Request(TestData.Finding("F-001")));

        Assert.NotEmpty(result.Details);
        Assert.All(result.Details, d => Assert.DoesNotContain("123-45", d));
    }

    [Fact]
    public void Validate_WrongTypeMessage_DoesNotQuoteOutput()
    {
        var raw = Mutate(o => o["key_concerns"] = new JsonArray(new JsonObject { ["concern"] = "x", ["cited_finding_ids"] = "SSN 123-45-6789" }));

        var result = ReviewOutputValidator.Validate(raw, TestData.Request(TestData.Finding("F-001")));

        AssertOnly(ValidationFailureCode.SchemaViolation, result);
        Assert.All(result.Details, d => Assert.DoesNotContain("123-45", d));
    }

    // VAL-6: the fallback must pass the same validator as Bedrock output.
    public static TheoryData<ReviewRequest> DeterministicRequests => new()
    {
        TestData.Request(),
        TestData.Request(TestData.Finding("F-001")),
        TestData.Request(
            TestData.Finding("F-001"),
            TestData.Finding("F-002", rule: "ADDRESS_MISMATCH", severity: FindingSeverity.Medium),
            TestData.Finding("F-003", rule: "MISSING_SIGNATURE", severity: FindingSeverity.Low)),
        TestData.Request(Enumerable.Range(1, 25)
            .Select(i => TestData.Finding($"F-{i:000}", rule: $"RULE_{i}", description: new string('d', 900)))
            .ToArray()),
        // Upstream text with confidence wording must not break the fallback.
        TestData.Request(TestData.Finding("F-001", description: "Textract confidence: 62 on TIN. High confidence mismatch, 95% certain.")),
        TestData.Request(TestData.Finding("F-001", rule: "TIN-CONFIDENCE-LOW")),
        TestData.Request(TestData.Finding("F-001", description: new string('d', 280) + " high confidences")),
        TestData.InjectionRequest(),
    };

    [Theory]
    [MemberData(nameof(DeterministicRequests))]
    public async Task DeterministicReviewer_Output_PassesValidator(ReviewRequest request)
    {
        var result = await new DeterministicReviewer().ReviewAsync(request, CancellationToken.None);

        var validation = ReviewOutputValidator.Validate(result.RawOutput, request);

        Assert.True(validation.IsValid, string.Join("; ", validation.Details));
    }

    private static void AssertOnly(ValidationFailureCode expected, ReviewValidationResult result)
    {
        Assert.False(result.IsValid);
        Assert.Null(result.Output);
        Assert.Equal(new[] { expected }, result.Errors);
    }

    private static JsonObject Concern(params string[] findingIds) => new()
    {
        ["concern"] = "The name on the W-9 does not match the account holder.",
        ["cited_finding_ids"] = new JsonArray(findingIds.Select(id => (JsonNode)id).ToArray()),
    };

    private static string ValidJson() => Mutate(_ => { });

    // Starts from a valid output that cites F-001, applies the change, and returns the JSON text.
    private static string Mutate(Action<JsonObject> change)
    {
        var output = new JsonObject
        {
            ["summary"] = "One high-severity finding was raised: the W-9 name does not match the account holder.",
            ["key_concerns"] = new JsonArray(Concern("F-001")),
            ["recommended_next_steps"] = new JsonArray("Request a corrected W-9."),
            ["draft_case_note"] = "W-9 name mismatch (F-001). Requested a corrected W-9.",
        };
        change(output);
        return output.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
    }
}
