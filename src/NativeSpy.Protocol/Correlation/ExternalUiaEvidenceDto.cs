using NativeSpy.Protocol.Common;

namespace NativeSpy.Protocol.Correlation;

/// <summary>
/// Detached observations captured from one external UI Automation source.
/// </summary>
public sealed class ExternalUiaEvidenceDto
{
    public ExternalUiaEvidenceDto(
        ExternalObservationRefDto source,
        int processId,
        ulong? observedHwnd,
        IEnumerable<CorrelationEvidenceFactDto> evidenceFacts,
        IEnumerable<CorrelationLimitationDto> limitations,
        OperationErrorDto? operationError = null)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        if (processId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(processId), processId, "ProcessId must be positive.");
        }

        if (observedHwnd == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(observedHwnd), observedHwnd, "An observed HWND must be positive when supplied.");
        }

        ProcessId = processId;
        ObservedHwnd = observedHwnd;
        EvidenceFacts = ContractValidation.CopyRequired(evidenceFacts, nameof(evidenceFacts));
        Limitations = ContractValidation.CopyRequired(limitations, nameof(limitations));
        OperationError = operationError;
    }

    public ExternalObservationRefDto Source { get; }

    public int ProcessId { get; }

    public ulong? ObservedHwnd { get; }

    public IReadOnlyList<CorrelationEvidenceFactDto> EvidenceFacts { get; }

    public IReadOnlyList<CorrelationLimitationDto> Limitations { get; }

    public OperationErrorDto? OperationError { get; }
}
