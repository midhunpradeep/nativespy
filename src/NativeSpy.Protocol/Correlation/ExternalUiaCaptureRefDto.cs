using NativeSpy.Protocol.Common;

namespace NativeSpy.Protocol.Correlation;

/// <summary>
/// Detached identity for one capture from a retained external UIA observation.
/// ObservationId identifies the retained source context; CaptureId identifies one attempt.
/// </summary>
public sealed class ExternalUiaCaptureRefDto
{
    public ExternalUiaCaptureRefDto(string observationId, string captureId)
    {
        ObservationId = ContractValidation.RequiredIdentifier(observationId, nameof(observationId));
        CaptureId = ContractValidation.RequiredIdentifier(captureId, nameof(captureId));
    }

    public string ObservationId { get; }

    public string CaptureId { get; }
}
