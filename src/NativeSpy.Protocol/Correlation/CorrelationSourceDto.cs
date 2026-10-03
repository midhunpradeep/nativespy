namespace NativeSpy.Protocol.Correlation;

public sealed class CorrelationSourceDto
{
    public CorrelationSourceDto(
        CorrelationSourceKind sourceKind,
        ExternalObservationRefDto? externalObservation = null,
        CorrelationTargetRefDto? nativeTarget = null)
    {
        SourceKind = sourceKind;
        ExternalObservation = externalObservation;
        NativeTarget = nativeTarget;

        var payloadCount = (externalObservation is null ? 0 : 1) + (nativeTarget is null ? 0 : 1);
        if (payloadCount != 1)
        {
            throw new ArgumentException("A correlation source must contain exactly one payload.");
        }

        var matches = sourceKind switch
        {
            CorrelationSourceKind.ExternalObservation => externalObservation is not null && nativeTarget is null,
            CorrelationSourceKind.NativeTarget => nativeTarget is not null && externalObservation is null,
            _ => false
        };
        if (!matches)
        {
            throw new ArgumentException("The source payload must match SourceKind.", nameof(sourceKind));
        }
    }

    public CorrelationSourceKind SourceKind { get; }

    public ExternalObservationRefDto? ExternalObservation { get; }

    public CorrelationTargetRefDto? NativeTarget { get; }
}
