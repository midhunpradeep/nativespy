using NativeSpy.Protocol.Correlation;

namespace NativeSpy.Client.Correlation;

internal enum CorrelationFailureClassification
{
    None,
    ExternalSelectionStale,
    CorrelationUnavailable
}

internal static class CorrelationFailureClassifier
{
    private static readonly HashSet<string> ExternalSelectionStaleCodes = new(StringComparer.Ordinal)
    {
        "ExternalSourceNotAvailable",
        "ExternalSourceUnavailableOrIncomplete",
        "ExternalEvidenceSourceMismatch",
        "InitialExternalEqualityFailed",
        "InitialExternalEvidenceMismatch",
        "ExternalEqualityRevalidationFailed",
        "ExternalRevalidationEvidenceMismatch"
    };

    public static CorrelationFailureClassification Classify(CorrelationResultDto correlation)
    {
        ArgumentNullException.ThrowIfNull(correlation);

        if (correlation.Status == CorrelationStatus.Exact)
        {
            return CorrelationFailureClassification.None;
        }

        if (correlation.OperationError is null
            && correlation.Limitations.Any(limitation =>
                ExternalSelectionStaleCodes.Contains(limitation.Code)))
        {
            return CorrelationFailureClassification.ExternalSelectionStale;
        }

        return CorrelationFailureClassification.CorrelationUnavailable;
    }
}
