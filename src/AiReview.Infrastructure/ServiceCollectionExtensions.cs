using AiReview.Core.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AiReview.Infrastructure;

public static class ServiceCollectionExtensions
{
    // Registers the in-memory review and approval stores. Records are lost when the process stops.
    public static IServiceCollection AddInMemoryStores(this IServiceCollection services)
    {
        services.TryAddSingleton<IReviewStore, InMemoryReviewStore>();
        services.TryAddSingleton<IApprovalStore, InMemoryApprovalStore>();
        return services;
    }
}
