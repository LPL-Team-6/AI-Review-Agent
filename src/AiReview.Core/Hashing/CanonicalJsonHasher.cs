using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AiReview.Core.Hashing;

/// <summary>
/// SHA-256 over a canonical JSON form: object properties sorted by name (ordinal), array order kept, no
/// whitespace. The same value always gives the same hash, whatever property order or formatting it arrived in.
/// Used for input_hash and content_hash (section 7.2), which approvals are bound to (APR-3).
/// </summary>
public static class CanonicalJsonHasher
{
    // Fixed options so the canonical form never depends on caller settings.
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    // Returns the lowercase hex SHA-256 of the value's canonical JSON.
    public static string Hash<T>(T value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(Canonicalize(value)));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    // Serializes the value with its JSON property names and returns it with every object's keys sorted.
    public static string Canonicalize<T>(T value)
    {
        var node = JsonSerializer.SerializeToNode(value, Options);
        return Sort(node)?.ToJsonString(Options) ?? "null";
    }

    // Returns a copy of the node with object keys in ordinal order at every level.
    private static JsonNode? Sort(JsonNode? node) => node switch
    {
        JsonObject obj => new JsonObject(obj
            .OrderBy(property => property.Key, StringComparer.Ordinal)
            .Select(property => KeyValuePair.Create(property.Key, Sort(property.Value)))),
        JsonArray array => new JsonArray(array.Select(Sort).ToArray()),
        _ => node?.DeepClone(),
    };
}
