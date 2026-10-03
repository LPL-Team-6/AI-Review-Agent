namespace AiReview.Providers.Bedrock;

/// <summary>Bound from the "AiReview:Bedrock" configuration section (spec 3.3). Nothing here is hard-coded per account.</summary>
public sealed class BedrockOptions
{
    public const string SectionName = "AiReview:Bedrock";

    /// <summary>A Claude model or inference profile ID. Required when the provider is "bedrock" (open item D-3).</summary>
    public string? ModelId { get; set; }

    /// <summary>For example "us-east-1". When empty, the SDK's default region chain is used.</summary>
    public string? Region { get; set; }

    /// <summary>A timeout counts as a failed attempt (TRANSPORT_ERROR).</summary>
    public int TimeoutSeconds { get; set; } = 30;

    public int MaxTokens { get; set; } = 1500;

    /// <summary>Low temperature for repeatability.</summary>
    public float Temperature { get; set; } = 0f;
}
