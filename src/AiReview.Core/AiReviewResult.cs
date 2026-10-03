namespace AiReview.Core;

/// <summary>
/// What one reviewer attempt returns. The output is kept as raw JSON text so that Bedrock and the
/// deterministic fallback go through the same strict validator (VAL-1, VAL-6).
/// </summary>
public sealed record AiReviewResult
{
    /// <summary>"bedrock" or "deterministic"; see <see cref="ReviewProviders"/>.</summary>
    public required string Provider { get; init; }

    /// <summary>The Bedrock model or inference profile ID; null for the deterministic reviewer.</summary>
    public string? ModelId { get; init; }

    /// <summary>The JSON text returned by the provider; null when the call itself failed.</summary>
    public string? RawOutput { get; init; }

    /// <summary>Short description of a Bedrock exception, throttle, timeout, or missing credentials. Never contains field values.</summary>
    public string? TransportError { get; init; }

    public TimeSpan Latency { get; init; }

    /// <summary>Token usage when the provider reports it (RT-4).</summary>
    public int? InputTokens { get; init; }

    public int? OutputTokens { get; init; }

    public bool IsTransportFailure => RawOutput is null;

    // Builds a result for a call that returned output (still to be validated by the caller).
    public static AiReviewResult Success(
        string provider,
        string rawOutput,
        TimeSpan latency,
        string? modelId = null,
        int? inputTokens = null,
        int? outputTokens = null) => new()
    {
        Provider = provider,
        ModelId = modelId,
        RawOutput = rawOutput,
        Latency = latency,
        InputTokens = inputTokens,
        OutputTokens = outputTokens,
    };

    // Builds a result for a call that produced no output; ReviewService records it as TRANSPORT_ERROR.
    public static AiReviewResult TransportFailure(string provider, string error, TimeSpan latency, string? modelId = null) => new()
    {
        Provider = provider,
        ModelId = modelId,
        TransportError = error,
        Latency = latency,
    };
}
