using System.Text.Json;
using AiReview.Contracts.Input;
using AiReview.Contracts.Output;
using AiReview.Core.Hashing;
using AiReview.Core.Validation;

namespace AiReview.Core.Tests;

public class CanonicalJsonHasherTests
{
    [Fact]
    public void Canonicalize_SortsKeysAtEveryLevel_AndKeepsArrayOrder()
    {
        var value = JsonSerializer.Deserialize<JsonElement>("""{ "b": [3, 1, {"z": 1, "a": 2}], "a": "x" }""");

        Assert.Equal("""{"a":"x","b":[3,1,{"a":2,"z":1}]}""", CanonicalJsonHasher.Canonicalize(value));
    }

    [Fact]
    public void Hash_IsLowercaseSha256Hex()
    {
        // sha256("{}")
        Assert.Equal(
            "44136fa355b3678a1146ad16f7e8649e94fb4fc21fe77e8310c060f61caaff8a",
            CanonicalJsonHasher.Hash(new Dictionary<string, int>()));
    }

    [Fact]
    public void Hash_ModelOutput_IgnoresPropertyOrderAndWhitespace()
    {
        var request = TestData.Request(TestData.Finding("F-001"));
        var compact = """{"summary":"S","key_concerns":[{"concern":"C","cited_finding_ids":["F-001"]}],"recommended_next_steps":["N"],"draft_case_note":"D"}""";
        var reordered = """
            {
              "draft_case_note": "D",
              "recommended_next_steps": [ "N" ],
              "key_concerns": [ { "cited_finding_ids": [ "F-001" ], "concern": "C" } ],
              "summary": "S"
            }
            """;

        var first = ReviewOutputValidator.Validate(compact, request).Output!;
        var second = ReviewOutputValidator.Validate(reordered, request).Output!;

        Assert.Equal(CanonicalJsonHasher.Hash(first), CanonicalJsonHasher.Hash(second));
    }

    [Fact]
    public void Hash_DifferentContent_GivesDifferentHash()
    {
        var output = new ReviewOutput
        {
            Summary = "S",
            KeyConcerns = Array.Empty<KeyConcern>(),
            RecommendedNextSteps = new[] { "N" },
            DraftCaseNote = "D",
        };

        Assert.NotEqual(CanonicalJsonHasher.Hash(output), CanonicalJsonHasher.Hash(output with { DraftCaseNote = "D." }));
    }

    [Fact]
    public void Hash_Request_UsesJsonPropertyNames()
    {
        var canonical = CanonicalJsonHasher.Canonicalize(TestData.Request());

        Assert.StartsWith("""{"case_id":"CASE-0001","documents":[{"document_id":"DOC-1","document_type":"W-9","fields":[{"field_id":""", canonical);
        Assert.Equal(CanonicalJsonHasher.Hash(TestData.Request()), CanonicalJsonHasher.Hash(TestData.Request()));
    }
}
