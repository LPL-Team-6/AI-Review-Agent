using System.Text.Json.Serialization;
using AiReview.Core.Input;
using AiReview.Core.Persistence;
using AiReview.Core.Services;

namespace AiReview.Api.Endpoints;

/// <summary>Body of PUT /cases/{caseId}/reviews/{version}/analyst-note.</summary>
public sealed record AnalystNoteRequest
{
    [JsonPropertyName("analyst_case_note")]
    public string? AnalystCaseNote { get; init; }
}

public static class ReviewEndpoints
{
    // Generate a review, read stored versions, and save the analyst's note.
    public static IEndpointRouteBuilder MapReviewEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/cases/{caseId}/reviews").WithTags("Reviews");

        group.MapPost("", GenerateAsync);
        group.MapGet("", ListAsync);
        group.MapGet("/latest", GetLatestAsync);
        group.MapGet("/{version:int}", GetVersionAsync);
        group.MapPut("/{version:int}/analyst-note", SetAnalystNoteAsync);

        return app;
    }

    // Generates the case's next review version. Always 201 for a valid request: Bedrock failures fall back (RT-3).
    private static async Task<IResult> GenerateAsync(string caseId, HttpRequest http, ReviewService reviews, CancellationToken ct)
    {
        // Read as text so the input contract's own reader produces the error list, not the framework binder.
        using var reader = new StreamReader(http.Body);
        var read = ReviewRequestReader.Read(await reader.ReadToEndAsync(ct));
        if (!read.IsValid)
        {
            return Results.BadRequest(new { errors = read.Errors });
        }
        if (!string.Equals(read.Request!.CaseId, caseId, StringComparison.Ordinal))
        {
            return Results.BadRequest(new { errors = new[] { "case_id in the body does not match the URL." } });
        }

        var record = await reviews.GenerateAsync(read.Request, ct);
        return Results.Created($"/cases/{Uri.EscapeDataString(caseId)}/reviews/{record.Version}", record);
    }

    // Every version of the case, oldest first.
    private static async Task<IResult> ListAsync(string caseId, IReviewStore store, CancellationToken ct)
    {
        var records = await store.ListAsync(caseId, ct);
        return records.Count == 0 ? Results.NotFound() : Results.Ok(records);
    }

    private static async Task<IResult> GetLatestAsync(string caseId, IReviewStore store, CancellationToken ct)
    {
        var record = await store.GetLatestAsync(caseId, ct);
        return record is null ? Results.NotFound() : Results.Ok(record);
    }

    private static async Task<IResult> GetVersionAsync(string caseId, int version, IReviewStore store, CancellationToken ct)
    {
        var record = await store.GetVersionAsync(caseId, version, ct);
        return record is null ? Results.NotFound() : Results.Ok(record);
    }

    // Saves analyst_case_note on the latest version only; older versions are read-only (APR-1, OUT-1).
    private static async Task<IResult> SetAnalystNoteAsync(
        string caseId, int version, AnalystNoteRequest body, ReviewService reviews, CancellationToken ct)
    {
        var (status, record) = await reviews.SetAnalystCaseNoteAsync(caseId, version, body.AnalystCaseNote, ct);
        return status switch
        {
            AnalystNoteUpdateStatus.Updated => Results.Ok(record),
            AnalystNoteUpdateStatus.NotFound => Results.NotFound(),
            AnalystNoteUpdateStatus.NotLatestVersion => Results.Conflict(new { error = "Only the latest review version can be edited." }),
            AnalystNoteUpdateStatus.TooLong => Results.BadRequest(new { errors = new[] { $"analyst_case_note is longer than {ReviewService.MaxAnalystCaseNoteLength} characters." } }),
            _ => throw new InvalidOperationException($"Unhandled status {status}."),
        };
    }
}
