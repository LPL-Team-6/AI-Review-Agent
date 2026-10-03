using AiReview.Contracts.Input;
using AiReview.Contracts.Output;
using AiReview.Core.Confidence;
using Microsoft.Extensions.Options;

namespace AiReview.Core.Tests;

public class ConfidenceCalculatorTests
{
    private readonly ConfidenceCalculator _calculator = new(Options.Create(new ConfidenceOptions()));

    [Fact]
    public void Calculate_UsesOnlyReferencedFields_WhenFindingsReferenceFields()
    {
        // name = 98.4 is referenced; tin = 71.0 is not.
        var result = _calculator.Calculate(TestData.Request(TestData.Finding("F-001", matchScore: 0.92, fieldIds: "DOC-1.name")));

        Assert.Equal(0.984, result.Extraction.Mean);
        Assert.Equal(0.984, result.Extraction.Min);
        Assert.Equal("DOC-1.name", result.Extraction.MinFieldId);
        Assert.Equal(1, result.Extraction.FieldsUsed);
        Assert.Equal(0.92, result.RuleMatch.Mean);
        Assert.Equal(1, result.RuleMatch.FindingsUsed);
        // 0.5 * 0.984 + 0.5 * 0.92 = 0.952; the cap (1.084) does not apply.
        Assert.Equal(0.952, result.Score);
        Assert.False(result.AppliedCap);
        Assert.Equal(ConfidenceBands.High, result.Band);
    }

    [Fact]
    public void Calculate_WeakestFieldCapsTheScore()
    {
        var result = _calculator.Calculate(TestData.Request(TestData.Finding("F-001", matchScore: 0.92, fieldIds: "DOC-1.tin")));

        // 0.5 * 0.71 + 0.5 * 0.92 = 0.815, capped at 0.71 + 0.10 = 0.81.
        Assert.Equal(0.81, result.Score);
        Assert.True(result.AppliedCap);
        Assert.Equal("DOC-1.tin", result.Extraction.MinFieldId);
        Assert.Equal(ConfidenceBands.Medium, result.Band);
    }

    [Fact]
    public void Calculate_NoFindings_UsesAllFieldsAndRuleMatchOfOne()
    {
        var result = _calculator.Calculate(TestData.Request());

        Assert.Equal(2, result.Extraction.FieldsUsed);
        Assert.Equal(0.847, result.Extraction.Mean);
        Assert.Equal(0.71, result.Extraction.Min);
        Assert.Equal(1.0, result.RuleMatch.Mean);
        Assert.Equal(0, result.RuleMatch.FindingsUsed);
        // 0.5 * 0.847 + 0.5 * 1.0 = 0.9235, capped at 0.81.
        Assert.Equal(0.81, result.Score);
        Assert.True(result.AppliedCap);
    }

    [Fact]
    public void Calculate_FieldReferencedByTwoFindings_CountsOnce()
    {
        var result = _calculator.Calculate(TestData.Request(
            TestData.Finding("F-001", matchScore: 0.9, fieldIds: new[] { "DOC-1.name", "DOC-1.tin" }),
            TestData.Finding("F-002", matchScore: 0.66, fieldIds: "DOC-1.tin")));

        Assert.Equal(2, result.Extraction.FieldsUsed);
        Assert.Equal(0.847, result.Extraction.Mean);
        Assert.Equal(0.78, result.RuleMatch.Mean);
        Assert.Equal(2, result.RuleMatch.FindingsUsed);
        // 0.5 * 0.847 + 0.5 * 0.78 = 0.8135, capped at 0.81.
        Assert.Equal(0.81, result.Score);
    }

    [Fact]
    public void Calculate_TiedLowestFields_NamesTheFirstInRequestOrder()
    {
        var request = TestData.Request() with
        {
            Documents = new[]
            {
                new DocumentDto
                {
                    DocumentId = "DOC-1",
                    DocumentType = "W-9",
                    Fields = new[] { TestData.Field("DOC-1.b", 80), TestData.Field("DOC-1.a", 80) },
                },
            },
        };

        Assert.Equal("DOC-1.b", _calculator.Calculate(request).Extraction.MinFieldId);
    }

    [Theory]
    [InlineData(0.70, 0.85, ConfidenceBands.High)]
    [InlineData(0.69, 0.845, ConfidenceBands.Medium)]
    [InlineData(0.30, 0.65, ConfidenceBands.Medium)]
    [InlineData(0.29, 0.645, ConfidenceBands.Low)]
    public void Calculate_BandBoundaries(double matchScore, double expectedScore, string expectedBand)
    {
        // Every field at 100 makes E = 1.0 and the cap 1.10, so score = 0.5 + 0.5 * matchScore.
        var request = AllFieldsAt(100, TestData.Finding("F-001", matchScore: matchScore));

        var result = _calculator.Calculate(request);

        Assert.Equal(expectedScore, result.Score);
        Assert.Equal(expectedBand, result.Band);
    }

    [Fact]
    public void Calculate_NoFields_IsLow()
    {
        var request = TestData.Request() with { Documents = Array.Empty<DocumentDto>() };

        var result = _calculator.Calculate(request);

        Assert.Equal(0, result.Extraction.FieldsUsed);
        Assert.Null(result.Extraction.MinFieldId);
        // 0.5 * 0 + 0.5 * 1.0 = 0.5, capped at 0 + 0.10.
        Assert.Equal(0.1, result.Score);
        Assert.Equal(ConfidenceBands.Low, result.Band);
    }

    [Fact]
    public void Calculate_SameInput_GivesSameResult()
    {
        var request = TestData.Request(TestData.Finding("F-001"), TestData.Finding("F-002", matchScore: 0.4, fieldIds: "DOC-1.tin"));

        // Records holding nested records compare by value.
        Assert.Equal(_calculator.Calculate(request), _calculator.Calculate(request));
    }

    [Fact]
    public void Calculate_CustomWeights_AreUsedAndDescribed()
    {
        var calculator = new ConfidenceCalculator(Options.Create(new ConfidenceOptions
        {
            ExtractionWeight = 0.7,
            RuleMatchWeight = 0.3,
            WeakestFieldMargin = 0.2,
        }));

        var result = calculator.Calculate(AllFieldsAt(90, TestData.Finding("F-001", matchScore: 0.5)));

        // 0.7 * 0.9 + 0.3 * 0.5 = 0.78; the cap (1.10) does not apply.
        Assert.Equal(0.78, result.Score);
        Assert.Equal("0.7*extraction + 0.3*rule_match, capped at min_field+0.20", result.Method);
    }

    [Fact]
    public void Method_DefaultOptions_MatchesSpecWording()
    {
        Assert.Equal(
            "0.5*extraction + 0.5*rule_match, capped at min_field+0.10",
            _calculator.Calculate(TestData.Request()).Method);
    }

    [Theory]
    [InlineData(0.6, 0.6, 0.1, 0.85, 0.65)]
    [InlineData(-0.5, 1.5, 0.1, 0.85, 0.65)]
    [InlineData(0.5, 0.5, -0.1, 0.85, 0.65)]
    [InlineData(0.5, 0.5, 0.1, 0.60, 0.65)]
    [InlineData(0.5, 0.5, 0.1, 1.20, 0.65)]
    public void Options_Invalid_AreReported(double e, double r, double margin, double high, double medium)
    {
        var options = new ConfidenceOptions
        {
            ExtractionWeight = e,
            RuleMatchWeight = r,
            WeakestFieldMargin = margin,
            HighThreshold = high,
            MediumThreshold = medium,
        };

        Assert.NotEmpty(options.Validate());
    }

    [Fact]
    public void Options_Defaults_AreValid()
    {
        Assert.Empty(new ConfidenceOptions().Validate());
    }

    private static ReviewRequest AllFieldsAt(double confidence, params RuleFinding[] findings) => TestData.Request(findings) with
    {
        Documents = new[]
        {
            new DocumentDto
            {
                DocumentId = "DOC-1",
                DocumentType = "W-9",
                Fields = new[] { TestData.Field("DOC-1.name", confidence), TestData.Field("DOC-1.tin", confidence) },
            },
        },
    };
}
