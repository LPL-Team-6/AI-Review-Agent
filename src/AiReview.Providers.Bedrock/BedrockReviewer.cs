using System.Diagnostics;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Amazon.Runtime;
using AiReview.Contracts.Input;
using AiReview.Core;
using AiReview.Core.Prompting;
using AiReview.Core.Validation;
using Microsoft.Extensions.Options;

namespace AiReview.Providers.Bedrock;

/// <summary>
/// Calls Amazon Bedrock with the Converse API and returns the raw response text. It does not validate,
/// retry, or fall back; ReviewService owns that flow (section 6). Any call failure becomes a transport failure.
/// </summary>
public sealed class BedrockReviewer : IAiReviewer
{
    private readonly IAmazonBedrockRuntime _client;
    private readonly BedrockOptions _options;
    private readonly PromptBuilder _promptBuilder;

    // Creates a reviewer using the given Bedrock client, configuration, and prompt builder.
    public BedrockReviewer(IAmazonBedrockRuntime client, IOptions<BedrockOptions> options, PromptBuilder promptBuilder)
    {
        _client = client;
        _options = options.Value;
        _promptBuilder = promptBuilder;
    }

    public string ProviderName => ReviewProviders.Bedrock;

    // Sends one Converse request; never throws for Bedrock failures, only when the caller cancels.
    public async Task<AiReviewResult> ReviewAsync(
        ReviewRequest request,
        CancellationToken ct,
        IReadOnlyCollection<ValidationFailureCode>? retryErrors = null)
    {
        var modelId = _options.ModelId;
        if (string.IsNullOrWhiteSpace(modelId))
        {
            return AiReviewResult.TransportFailure(ProviderName, "Bedrock model ID is not configured.", TimeSpan.Zero);
        }

        var converseRequest = new ConverseRequest
        {
            ModelId = modelId,
            System = new List<SystemContentBlock> { new() { Text = _promptBuilder.BuildSystemPrompt() } },
            Messages = new List<Message>
            {
                new()
                {
                    Role = ConversationRole.User,
                    Content = new List<ContentBlock> { new() { Text = _promptBuilder.BuildUserMessage(request, retryErrors) } },
                },
            },
            InferenceConfig = new InferenceConfiguration
            {
                MaxTokens = _options.MaxTokens,
                Temperature = _options.Temperature,
            },
        };

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));
        var stopwatch = Stopwatch.StartNew();

        ConverseResponse response;
        try
        {
            response = await _client.ConverseAsync(converseRequest, timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return AiReviewResult.TransportFailure(ProviderName, $"Timed out after {_options.TimeoutSeconds}s.", stopwatch.Elapsed, modelId);
        }
        catch (AmazonServiceException ex)
        {
            // Throttling, access denied, validation, and model errors. ErrorCode never contains prompt content.
            return AiReviewResult.TransportFailure(ProviderName, $"Bedrock error: {ex.ErrorCode ?? ex.GetType().Name}.", stopwatch.Elapsed, modelId);
        }
        catch (AmazonClientException ex)
        {
            // Missing credentials, no region, or network failures before a response arrived.
            return AiReviewResult.TransportFailure(ProviderName, $"Bedrock client error: {ex.GetType().Name}.", stopwatch.Elapsed, modelId);
        }

        var text = string.Concat(
            response.Output?.Message?.Content?.Select(block => block.Text).Where(t => t is not null) ?? Enumerable.Empty<string>());
        if (text.Length == 0)
        {
            return AiReviewResult.TransportFailure(ProviderName, $"Bedrock returned no text (stop reason: {response.StopReason}).", stopwatch.Elapsed, modelId);
        }

        return AiReviewResult.Success(
            ProviderName,
            text,
            stopwatch.Elapsed,
            modelId,
            response.Usage?.InputTokens,
            response.Usage?.OutputTokens);
    }
}
