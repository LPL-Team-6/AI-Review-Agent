using AiReview.Api.Endpoints;
using AiReview.Core;
using AiReview.Infrastructure;
using AiReview.Providers.Bedrock;
using AiReview.Providers.Deterministic;

var builder = WebApplication.CreateBuilder(args);

// AWS credentials come from the SDK's default chain, never from config (NFR-3). Without them, every
// Bedrock attempt fails as TRANSPORT_ERROR and the deterministic fallback is stored.
builder.Services
    .AddReviewCore(builder.Configuration)
    .AddBedrockReviewer(builder.Configuration)
    .AddDeterministicReviewer()
    .AddInMemoryStores();

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new { service = "AI Review Agent", status = "ok" }));
app.MapReviewEndpoints();
app.MapApprovalEndpoints();

app.Run();

// Lets integration tests start the app with WebApplicationFactory<Program>.
public partial class Program;
