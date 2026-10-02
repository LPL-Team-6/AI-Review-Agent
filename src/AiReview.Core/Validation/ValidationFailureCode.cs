namespace AiReview.Core.Validation;

/// <summary>Reasons a review attempt fails (spec section 6).</summary>
public enum ValidationFailureCode
{
    TransportError,
    MalformedJson,
    SchemaViolation,
    UnknownFindingId,
    UncitedConcern,
    ModelConfidence,
}

public static class ValidationFailureCodeExtensions
{
    // Returns the spec's wire name for the code, e.g. UnknownFindingId -> "UNKNOWN_FINDING_ID".
    public static string ToCode(this ValidationFailureCode code) => code switch
    {
        ValidationFailureCode.TransportError => "TRANSPORT_ERROR",
        ValidationFailureCode.MalformedJson => "MALFORMED_JSON",
        ValidationFailureCode.SchemaViolation => "SCHEMA_VIOLATION",
        ValidationFailureCode.UnknownFindingId => "UNKNOWN_FINDING_ID",
        ValidationFailureCode.UncitedConcern => "UNCITED_CONCERN",
        ValidationFailureCode.ModelConfidence => "MODEL_CONFIDENCE",
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, null),
    };
}
