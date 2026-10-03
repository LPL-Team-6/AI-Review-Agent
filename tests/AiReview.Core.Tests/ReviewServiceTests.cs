using Amazon.BedrockRuntime.Model;
using AiReview.Contracts.Input;
using AiReview.Core.Confidence;
using AiReview.Core.Hashing;
using AiReview.Core.Prompting;
using AiReview.Core.Services;
using AiReview.Infrastructure;
using AiReview.Providers.Bedrock;
using AiReview.Providers.Deterministic;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiReview.Core.Tests;

public class ReviewServiceTests
{
    private const string ModelId = "us.anthropic.claude-sonnet-4-6";
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeBedrockClient _bedrock = new();
    private readonly InMemoryReviewStore _store = new();
    private readonly CapturingLogger<ReviewService> _logger = new();
    private readonly ReviewRequest _request = TestData.Request(TestData.Finding("F-001"));

    [Fact]
    public async Task Generate_ValidFirstTry_StoresBedrockReview()
    {
        _bedrock.Returns(ModelOutput.Valid("F-001"));

        var record = await Service().GenerateAsync(_request, CancellationToken.None);

        Assert.Equal("bedrock", record.Provider);
        Assert.False(record.IsFallback);
        Assert.Null(record.FallbackReason);
        Assert.Equal(ModelId, record.ModelId);
        Assert.Equal(1, record.Attempts);
        Assert.Equal(1, record.Version);
        Assert.Equal(PromptBuilder.PromptVersion, record.PromptVersion);
        Assert.Equal(Now, record.CreatedAt);
        Assert.Equal(CanonicalJsonHasher.Hash(_request), record.InputHash);
        Assert.Equal(CanonicalJsonHasher.Hash(record.Output), record.ContentHash);
        Assert.Null(record.AnalystCaseNote);
        Assert.Single(_bedrock.Requests);
        Assert.Same(record, await _store.GetLatestAsync("CASE-0001", CancellationToken.None));
    }

    [Theory]
    [InlineData("```json\n{0}\n```")]
    [InlineData("```\n{0}\n```\n")]
    [InlineData("  ```JSON\r\n{0}\r\n```")]
    public async Task Generate_OutputInsideOneCodeFence_IsAccepted(string format)
    {
        // Seen from Claude Sonnet 4.6 on Bedrock: the fence survives the prompt rule and the retry.
        _bedrock.Returns(string.Format(format, ModelOutput.Valid("F-001")));

        var record = await Service().GenerateAsync(_request, CancellationToken.None);

        Assert.Equal("bedrock", record.Provider);
        Assert.Equal(1, record.Attempts);
    }

    [Theory]
    [InlineData("Here is the review:\n```json\n{0}\n```")]
    [InlineData("```json\n{0}\n```\nLet me know if you need more.")]
    [InlineData("```json\n{0}\n```\n```json\n{0}\n```")]
    public async Task Generate_FenceWithTextAround_IsStillMalformed(string format)
    {
        _bedrock
            .Returns(string.Format(format, ModelOutput.Valid("F-001")))
            .Returns(string.Format(format, ModelOutput.Valid("F-001")));

        var record = await Service().GenerateAsync(_request, CancellationToken.None);

        Assert.True(record.IsFallback);
        Assert.Equal("MALFORMED_JSON", record.FallbackReason);
    }

    [Fact]
    public async Task Generate_InvalidThenValid_RetriesWithErrorCodesOnly()
    {
        _bedrock
            .Returns("Sure! PREVIOUS-OUTPUT-MARKER " + ModelOutput.Valid("F-001"))
            .Returns(ModelOutput.Valid("F-001"));

        var record = await Service().GenerateAsync(_request, CancellationToken.None);

        Assert.Equal("bedrock", record.Provider);
        Assert.Equal(2, record.Attempts);
        Assert.False(record.IsFallback);

        var messages = _bedrock.UserMessages.ToList();
        Assert.DoesNotContain("rejected", messages[0]);
        Assert.Contains("MALFORMED_JSON", messages[1]);
        // RT-2: the previous invalid output is never sent back.
        Assert.DoesNotContain("PREVIOUS-OUTPUT-MARKER", messages[1]);
    }

    [Fact]
    public async Task Generate_TwoInvalidAttempts_FallsBackToDeterministic()
    {
        _bedrock
            .Returns("not json")
            .Returns(ModelOutput.Valid("F-999"));

        var record = await Service().GenerateAsync(_request, CancellationToken.None);

        Assert.Equal("deterministic", record.Provider);
        Assert.True(record.IsFallback);
        Assert.Equal("MALFORMED_JSON, UNKNOWN_FINDING_ID", record.FallbackReason);
        Assert.Equal(2, record.Attempts);
        Assert.Null(record.ModelId);
        Assert.Equal(2, _bedrock.Requests.Count);
        Assert.Contains("generated from rules, not AI", record.Output.Summary);
        Assert.Contains("MALFORMED_JSON", _bedrock.UserMessages.Last());
    }

    [Fact]
    public async Task Generate_ThrottledTwice_FallsBackWithTransportError()
    {
        _bedrock
            .Throws(new ThrottlingException("Rate exceeded"))
            .Throws(new ThrottlingException("Rate exceeded"));

        var record = await Service().GenerateAsync(_request, CancellationToken.None);

        Assert.True(record.IsFallback);
        Assert.Equal("TRANSPORT_ERROR", record.FallbackReason);
        Assert.Equal(2, record.Attempts);
        Assert.Contains("TRANSPORT_ERROR", _bedrock.UserMessages.Last());
    }

    [Fact]
    public async Task Generate_UnexpectedClientException_CountsAsTransportError()
    {
        _bedrock
            .Throws(new InvalidOperationException("SDK bug"))
            .Returns(ModelOutput.Valid("F-001"));

        var record = await Service().GenerateAsync(_request, CancellationToken.None);

        Assert.Equal("bedrock", record.Provider);
        Assert.Equal(2, record.Attempts);
    }

    [Fact]
    public async Task Generate_Timeout_CountsAsFailedAttempt()
    {
        _bedrock.Hangs();

        var record = await Service(maxAttempts: 1, timeoutSeconds: 1).GenerateAsync(_request, CancellationToken.None);

        Assert.True(record.IsFallback);
        Assert.Equal("TRANSPORT_ERROR", record.FallbackReason);
        Assert.Equal(1, record.Attempts);
    }

    [Fact]
    public async Task Generate_MaxAttemptsOne_DoesNotRetry()
    {
        _bedrock.Returns("not json");

        var record = await Service(maxAttempts: 1).GenerateAsync(_request, CancellationToken.None);

        Assert.True(record.IsFallback);
        Assert.Equal(1, record.Attempts);
        Assert.Single(_bedrock.Requests);
    }

    [Fact]
    public async Task Generate_ProviderDeterministic_NeverCallsBedrock()
    {
        var record = await Service(provider: "deterministic").GenerateAsync(_request, CancellationToken.None);

        Assert.Empty(_bedrock.Requests);
        Assert.Equal("deterministic", record.Provider);
        Assert.True(record.IsFallback);
        Assert.Equal(ReviewService.ConfiguredDeterministicReason, record.FallbackReason);
        Assert.Equal(0, record.Attempts);
    }

    [Fact]
    public async Task Generate_NoBedrockReviewer_UsesDeterministic()
    {
        var record = await Service(withBedrock: false).GenerateAsync(_request, CancellationToken.None);

        Assert.Equal("deterministic", record.Provider);
        Assert.Equal(ReviewService.BedrockUnavailableReason, record.FallbackReason);
    }

    [Fact]
    public async Task Generate_EachRun_AddsAVersion_WithTheSameConfidenceWhicheverProvider()
    {
        _bedrock
            .Returns(ModelOutput.Valid("F-001"))
            .Returns("not json")
            .Returns("not json");
        var service = Service();

        var first = await service.GenerateAsync(_request, CancellationToken.None);
        var second = await service.GenerateAsync(_request, CancellationToken.None);

        Assert.Equal((1, "bedrock"), (first.Version, first.Provider));
        Assert.Equal((2, "deterministic"), (second.Version, second.Provider));
        Assert.NotEqual(first.ReviewId, second.ReviewId);
        Assert.NotEqual(first.ContentHash, second.ContentHash);
        // CONF-1 and the input hash depend only on the request.
        Assert.Equal(first.Confidence, second.Confidence);
        Assert.Equal(first.InputHash, second.InputHash);
        Assert.Equal(2, (await _store.ListAsync("CASE-0001", CancellationToken.None)).Count);
    }

    [Fact]
    public async Task Generate_Cancelled_Throws_AndStoresNothing()
    {
        _bedrock.Hangs();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service().GenerateAsync(_request, cts.Token));

        Assert.Null(await _store.GetLatestAsync("CASE-0001", CancellationToken.None));
    }

    [Fact]
    public async Task Generate_InvalidRequest_Throws()
    {
        var bad = _request with { Findings = new[] { TestData.Finding("F-001", fieldIds: "DOC-9.missing") } };

        await Assert.ThrowsAsync<ArgumentException>(() => Service().GenerateAsync(bad, CancellationToken.None));
        Assert.Empty(_bedrock.Requests);
    }

    [Fact]
    public async Task Generate_Logs_EveryAttempt_WithoutFieldValuesOrOutputText()
    {
        // The model echoes a field value, and the second output also states confidence.
        _bedrock
            .Throws(new ThrottlingException("Rate exceeded"))
            .Returns(ModelOutput.Build(new[] { "F-001" }, "High confidence: TIN 123-45-6789 belongs to Jane Doe."));

        await Service().GenerateAsync(TestData.InjectionRequest(), CancellationToken.None);

        var logs = _logger.AllText;
        Assert.Contains("attempt 1 for case INJ-1", logs);
        Assert.Contains("attempt 2 for case INJ-1", logs);
        Assert.Contains("TRANSPORT_ERROR", logs);
        Assert.Contains("MODEL_CONFIDENCE", logs);
        Assert.Contains("1200 input / 300 output tokens", logs);
        Assert.DoesNotContain("Jane Doe", logs);
        Assert.DoesNotContain("123-45-6789", logs);
        Assert.DoesNotContain("ignore prior rules", logs);
    }

    [Fact]
    public async Task SetAnalystCaseNote_LatestVersion_UpdatesOnlyTheNote()
    {
        var service = Service(provider: "deterministic");
        var created = await service.GenerateAsync(_request, CancellationToken.None);

        var (status, updated) = await service.SetAnalystCaseNoteAsync("CASE-0001", 1, "Called the customer; W-9 re-sent.", CancellationToken.None);

        Assert.Equal(AnalystNoteUpdateStatus.Updated, status);
        Assert.Equal("Called the customer; W-9 re-sent.", updated!.AnalystCaseNote);
        // OUT-1: the model-authored output and its hash are untouched.
        Assert.Same(created.Output, updated.Output);
        Assert.Equal(created.ContentHash, updated.ContentHash);
    }

    [Fact]
    public async Task SetAnalystCaseNote_OlderVersion_IsReadOnly()
    {
        var service = Service(provider: "deterministic");
        await service.GenerateAsync(_request, CancellationToken.None);
        await service.GenerateAsync(_request, CancellationToken.None);

        Assert.Equal(AnalystNoteUpdateStatus.NotLatestVersion, (await service.SetAnalystCaseNoteAsync("CASE-0001", 1, "x", CancellationToken.None)).Status);
        Assert.Equal(AnalystNoteUpdateStatus.NotFound, (await service.SetAnalystCaseNoteAsync("CASE-0001", 3, "x", CancellationToken.None)).Status);
        Assert.Equal(AnalystNoteUpdateStatus.NotFound, (await service.SetAnalystCaseNoteAsync("CASE-0404", 1, "x", CancellationToken.None)).Status);
        Assert.Equal(AnalystNoteUpdateStatus.TooLong, (await service.SetAnalystCaseNoteAsync("CASE-0001", 2, new string('x', 4001), CancellationToken.None)).Status);
    }

    [Fact]
    public void Options_RejectMoreThanTwoAttemptsAndUnknownProviders()
    {
        Assert.NotEmpty(new ReviewServiceOptions { MaxAttempts = 3 }.Validate());
        Assert.NotEmpty(new ReviewServiceOptions { MaxAttempts = 0 }.Validate());
        Assert.NotEmpty(new ReviewServiceOptions { Provider = "openai" }.Validate());
        Assert.Empty(new ReviewServiceOptions { Provider = "Deterministic", MaxAttempts = 1 }.Validate());
    }

    private ReviewService Service(string provider = "bedrock", int maxAttempts = 2, int timeoutSeconds = 30, bool withBedrock = true)
    {
        var bedrock = new BedrockReviewer(
            _bedrock,
            Options.Create(new BedrockOptions { ModelId = ModelId, TimeoutSeconds = timeoutSeconds }),
            new PromptBuilder(new InputSanitizer()));

        return new ReviewService(
            withBedrock ? bedrock : null,
            new DeterministicReviewer(),
            _store,
            new ConfidenceCalculator(Options.Create(new ConfidenceOptions())),
            Options.Create(new ReviewServiceOptions { Provider = provider, MaxAttempts = maxAttempts }),
            new FixedTime(Now),
            _logger);
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
