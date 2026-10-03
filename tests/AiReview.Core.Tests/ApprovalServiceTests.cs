using AiReview.Contracts.Approval;
using AiReview.Contracts.Output;
using AiReview.Core.Confidence;
using AiReview.Core.Services;
using AiReview.Infrastructure;
using AiReview.Providers.Deterministic;
using Microsoft.Extensions.Options;

namespace AiReview.Core.Tests;

public class ApprovalServiceTests
{
    private readonly InMemoryReviewStore _reviews = new();
    private readonly InMemoryApprovalStore _approvals = new();
    private readonly ApprovalService _service;
    private readonly ReviewService _generator;

    public ApprovalServiceTests()
    {
        _service = new ApprovalService(_reviews, _approvals, TimeProvider.System);
        _generator = new ReviewService(
            bedrock: null,
            new DeterministicReviewer(),
            _reviews,
            new ConfidenceCalculator(Options.Create(new ConfidenceOptions())),
            Options.Create(new ReviewServiceOptions { Provider = "deterministic" }),
            TimeProvider.System,
            new CapturingLogger<ReviewService>());
    }

    [Fact]
    public async Task Approve_LatestReviewAndHash_IsRecorded()
    {
        var review = await Generate();

        var result = await _service.ApproveAsync("CASE-0001", Approve(review), CancellationToken.None);

        Assert.Equal(ApprovalOutcome.Approved, result.Outcome);
        Assert.Equal(review.ReviewId, result.Approval!.ReviewId);
        Assert.Equal(1, result.Approval.Version);
        Assert.Equal(review.ContentHash, result.Approval.ContentHash);
        Assert.Equal("analyst-7", result.Approval.ApprovedBy);

        var status = await _service.GetStatusAsync("CASE-0001", CancellationToken.None);
        Assert.True(status!.IsApproved);
        Assert.Null(status.ApprovalNeeded);
        Assert.Equal(result.Approval, status.CurrentApproval);
    }

    [Fact]
    public async Task Approve_HashMismatch_IsConflict()
    {
        var review = await Generate();

        var result = await _service.ApproveAsync("CASE-0001", Approve(review) with { ContentHash = new string('0', 64) }, CancellationToken.None);

        Assert.Equal(ApprovalOutcome.Conflict, result.Outcome);
        Assert.Empty(await _approvals.ListAsync("CASE-0001", CancellationToken.None));
    }

    [Fact]
    public async Task Approve_ReviewIdMismatch_IsConflict()
    {
        var review = await Generate();

        var result = await _service.ApproveAsync("CASE-0001", Approve(review) with { ReviewId = Guid.NewGuid() }, CancellationToken.None);

        Assert.Equal(ApprovalOutcome.Conflict, result.Outcome);
    }

    [Fact]
    public async Task Approve_EarlierVersionAfterRegeneration_IsConflict()
    {
        var v1 = await Generate();
        var v2 = await Generate();

        var result = await _service.ApproveAsync("CASE-0001", Approve(v1), CancellationToken.None);

        Assert.Equal(ApprovalOutcome.Conflict, result.Outcome);
        Assert.Contains("v2", result.Error);
        Assert.Equal(2, v2.Version);
    }

    [Fact]
    public async Task Regenerate_MakesEarlierApprovalStale()
    {
        var v1 = await Generate();
        var approval = (await _service.ApproveAsync("CASE-0001", Approve(v1), CancellationToken.None)).Approval!;

        await Generate();
        var status = await _service.GetStatusAsync("CASE-0001", CancellationToken.None);

        Assert.False(status!.IsApproved);
        Assert.Equal(2, status.LatestVersion);
        Assert.Equal("Approval needed for v2", status.ApprovalNeeded);
        Assert.Null(status.CurrentApproval);
        // APR-4: the stale approval is kept for audit.
        Assert.Equal(new[] { approval }, status.StaleApprovals);
    }

    [Fact]
    public async Task Approve_AfterRegeneration_ApprovesTheNewVersion()
    {
        await Generate();
        var v2 = await Generate();

        var result = await _service.ApproveAsync("CASE-0001", Approve(v2), CancellationToken.None);
        var status = await _service.GetStatusAsync("CASE-0001", CancellationToken.None);

        Assert.Equal(ApprovalOutcome.Approved, result.Outcome);
        Assert.True(status!.IsApproved);
        Assert.Equal(2, status.CurrentApproval!.Version);
    }

    [Fact]
    public async Task Approve_CaseWithoutReview_IsNotFound()
    {
        var result = await _service.ApproveAsync(
            "CASE-0404",
            new ApprovalRequest { ReviewId = Guid.NewGuid(), ContentHash = "abc", ApprovedBy = "analyst-7" },
            CancellationToken.None);

        Assert.Equal(ApprovalOutcome.NotFound, result.Outcome);
        Assert.Null(await _service.GetStatusAsync("CASE-0404", CancellationToken.None));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\u0007")]
    public async Task Approve_WithoutApprover_IsInvalid(string approvedBy)
    {
        var review = await Generate();

        var result = await _service.ApproveAsync("CASE-0001", Approve(review) with { ApprovedBy = approvedBy }, CancellationToken.None);

        Assert.Equal(ApprovalOutcome.Invalid, result.Outcome);
    }

    [Fact]
    public async Task Approve_DoesNotChangeTheReview()
    {
        var review = await Generate();

        await _service.ApproveAsync("CASE-0001", Approve(review), CancellationToken.None);

        Assert.Equal(review, await _reviews.GetLatestAsync("CASE-0001", CancellationToken.None));
    }

    private Task<ReviewRecord> Generate() =>
        _generator.GenerateAsync(TestData.Request(TestData.Finding("F-001")), CancellationToken.None);

    private static ApprovalRequest Approve(ReviewRecord review) => new()
    {
        ReviewId = review.ReviewId,
        ContentHash = review.ContentHash,
        ApprovedBy = "analyst-7",
    };
}
