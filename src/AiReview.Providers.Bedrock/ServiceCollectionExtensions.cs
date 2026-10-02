using Amazon;
using Amazon.BedrockRuntime;
using AiReview.Core;
using AiReview.Core.Prompting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace AiReview.Providers.Bedrock;

public static class ServiceCollectionExtensions
{
    // Registers BedrockReviewer as the keyed IAiReviewer "bedrock". Credentials come from the standard SDK chain (NFR-3).
    public static IServiceCollection AddBedrockReviewer(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<BedrockOptions>(configuration.GetSection(BedrockOptions.SectionName));

        services.TryAddSingleton<IAmazonBedrockRuntime>(sp =>
        {
            var region = sp.GetRequiredService<IOptions<BedrockOptions>>().Value.Region;
            return string.IsNullOrWhiteSpace(region)
                ? new AmazonBedrockRuntimeClient()
                : new AmazonBedrockRuntimeClient(RegionEndpoint.GetBySystemName(region));
        });

        services.TryAddSingleton<InputSanitizer>(_ => new InputSanitizer());
        services.TryAddSingleton<PromptBuilder>();
        services.AddKeyedSingleton<IAiReviewer, BedrockReviewer>(ReviewProviders.Bedrock);
        return services;
    }
}
