using AiReview.Core;
using Microsoft.Extensions.DependencyInjection;

namespace AiReview.Providers.Deterministic;

public static class ServiceCollectionExtensions
{
    // Registers DeterministicReviewer as the keyed IAiReviewer "deterministic", used for fallback and offline demos.
    public static IServiceCollection AddDeterministicReviewer(this IServiceCollection services)
    {
        services.AddKeyedSingleton<IAiReviewer, DeterministicReviewer>(ReviewProviders.Deterministic);
        return services;
    }
}
