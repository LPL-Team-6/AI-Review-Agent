using AiReview.Core.Confidence;
using AiReview.Core.Persistence;
using AiReview.Core.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiReview.Core;

public static class ServiceCollectionExtensions
{
    // Registers ReviewService, ApprovalService, and the confidence calculator. Reviewers come from the provider
    // packages (keyed "bedrock" and "deterministic") and stores from the infrastructure package.
    public static IServiceCollection AddReviewCore(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ReviewServiceOptions>()
            .Bind(configuration.GetSection(ReviewServiceOptions.SectionName))
            .Validate(o => o.Validate().Count == 0, "Invalid AiReview settings: Provider must be bedrock or deterministic, and MaxAttempts 1-2.")
            .ValidateOnStart();

        services.AddOptions<ConfidenceOptions>()
            .Bind(configuration.GetSection(ConfidenceOptions.SectionName))
            .Validate(o => o.Validate().Count == 0, "Invalid AiReview:Confidence settings: weights must be non-negative and add up to 1, thresholds 0-1.")
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ConfidenceCalculator>();
        services.TryAddSingleton<ApprovalService>();

        // The Bedrock reviewer is optional, so the keyed lookups are done here rather than in the constructor.
        services.TryAddSingleton(sp => new ReviewService(
            sp.GetKeyedService<IAiReviewer>(ReviewProviders.Bedrock),
            sp.GetRequiredKeyedService<IAiReviewer>(ReviewProviders.Deterministic),
            sp.GetRequiredService<IReviewStore>(),
            sp.GetRequiredService<ConfidenceCalculator>(),
            sp.GetRequiredService<IOptions<ReviewServiceOptions>>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<ReviewService>>()));

        return services;
    }
}
