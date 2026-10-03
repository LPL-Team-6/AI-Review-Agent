using AiReview.Contracts.Approval;
using AiReview.Core.Services;

namespace AiReview.Api.Endpoints;

public static class ApprovalEndpoints
{
    // Approve the latest review version and read the case's approval status.
    public static IEndpointRouteBuilder MapApprovalEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/cases/{caseId}/approvals").WithTags("Approvals");

        group.MapPost("", ApproveAsync);
        group.MapGet("", GetStatusAsync);

        return app;
    }

    // 201 when review_id and content_hash match the latest version; 409 when they do not (APR-3).
    // This records a sign-off on the review only. It never changes case state (APR-6).
    private static async Task<IResult> ApproveAsync(string caseId, ApprovalRequest body, ApprovalService approvals, CancellationToken ct)
    {
        var result = await approvals.ApproveAsync(caseId, body, ct);
        return result.Outcome switch
        {
            ApprovalOutcome.Approved => Results.Created($"/cases/{Uri.EscapeDataString(caseId)}/approvals", result.Approval),
            ApprovalOutcome.NotFound => Results.NotFound(new { error = result.Error }),
            ApprovalOutcome.Conflict => Results.Conflict(new { error = result.Error }),
            ApprovalOutcome.Invalid => Results.BadRequest(new { errors = new[] { result.Error } }),
            _ => throw new InvalidOperationException($"Unhandled outcome {result.Outcome}."),
        };
    }

    // Whether the latest version is approved, plus stale approvals kept for audit (APR-4).
    private static async Task<IResult> GetStatusAsync(string caseId, ApprovalService approvals, CancellationToken ct)
    {
        var status = await approvals.GetStatusAsync(caseId, ct);
        return status is null ? Results.NotFound() : Results.Ok(status);
    }
}
