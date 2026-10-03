using System.Text.Json.Nodes;
using Amazon;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Amazon.Runtime;
using Microsoft.Extensions.Logging;

namespace AiReview.Core.Tests;

/// <summary>
/// A Bedrock client whose ConverseAsync replies from a script instead of the network. It subclasses the real
/// client, so BedrockReviewer runs unchanged against it.
/// </summary>
internal sealed class FakeBedrockClient : AmazonBedrockRuntimeClient
{
    private readonly Queue<Func<CancellationToken, Task<ConverseResponse>>> _replies = new();

    public FakeBedrockClient()
        : base(new AnonymousAWSCredentials(), RegionEndpoint.USEast1)
    {
    }

    public List<ConverseRequest> Requests { get; } = new();

    // The user message text of each call, in order.
    public IEnumerable<string> UserMessages => Requests.Select(r => r.Messages.Single().Content.Single().Text);

    public FakeBedrockClient Returns(string text)
    {
        _replies.Enqueue(_ => Task.FromResult(Response(text)));
        return this;
    }

    public FakeBedrockClient Throws(Exception ex)
    {
        _replies.Enqueue(_ => Task.FromException<ConverseResponse>(ex));
        return this;
    }

    // Never answers; only the reviewer's timeout or the caller's token ends the call.
    public FakeBedrockClient Hangs()
    {
        _replies.Enqueue(async ct =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("unreachable");
        });
        return this;
    }

    public override Task<ConverseResponse> ConverseAsync(ConverseRequest request, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        if (_replies.Count == 0)
        {
            throw new InvalidOperationException("The fake Bedrock client has no scripted reply left.");
        }
        return _replies.Dequeue()(cancellationToken);
    }

    private static ConverseResponse Response(string text) => new()
    {
        Output = new ConverseOutput
        {
            Message = new Message
            {
                Role = ConversationRole.Assistant,
                Content = new List<ContentBlock> { new() { Text = text } },
            },
        },
        StopReason = StopReason.End_turn,
        Usage = new TokenUsage { InputTokens = 1200, OutputTokens = 300, TotalTokens = 1500 },
    };
}

/// <summary>Keeps every formatted log message so tests can check what was (and was not) logged.</summary>
internal sealed class CapturingLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, string Message)> Entries { get; } = new();

    public string AllText => string.Join("\n", Entries.Select(e => e.Message));

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        Entries.Add((logLevel, formatter(state, exception) + (exception is null ? "" : "\n" + exception)));
    }
}

/// <summary>Model output in the section 7.1 shape.</summary>
internal static class ModelOutput
{
    // A valid output with one concern citing the given IDs (none for a no-findings case).
    public static string Valid(params string[] citedFindingIds) => Build(citedFindingIds, "The W-9 name does not match the account holder.");

    public static string Build(string[] citedFindingIds, string summary)
    {
        var concerns = new JsonArray();
        if (citedFindingIds.Length > 0)
        {
            concerns.Add(new JsonObject
            {
                ["concern"] = "Name mismatch between the W-9 and the account record.",
                ["cited_finding_ids"] = new JsonArray(citedFindingIds.Select(id => (JsonNode)id).ToArray()),
            });
        }

        return new JsonObject
        {
            ["summary"] = summary,
            ["key_concerns"] = concerns,
            ["recommended_next_steps"] = new JsonArray("Request a corrected W-9."),
            ["draft_case_note"] = "Name mismatch raised by the rules engine. Corrected W-9 requested.",
        }.ToJsonString();
    }
}
