using NativeSpy.Protocol.Common;

namespace NativeSpy.Protocol.Correlation;

public sealed class ExternalObservationRefDto
{
    public ExternalObservationRefDto(string observationId, string captureId)
    {
        ObservationId = ContractValidation.RequiredIdentifier(observationId, nameof(observationId));
        CaptureId = ContractValidation.RequiredIdentifier(captureId, nameof(captureId));
    }

    public string ObservationId { get; }

    public string CaptureId { get; }
}
