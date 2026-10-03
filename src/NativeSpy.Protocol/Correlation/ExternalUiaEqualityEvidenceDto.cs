using NativeSpy.Protocol.Common;

namespace NativeSpy.Protocol.Correlation;

/// <summary>
/// Detached result of one UI Automation equality operation against an observed HWND.
/// </summary>
public sealed class ExternalUiaEqualityEvidenceDto
{
    public ExternalUiaEqualityEvidenceDto(
        ExternalUiaCaptureRefDto source,
        ulong hwnd,
        IEnumerable<CorrelationEvidenceFactDto> evidenceFacts,
        IEnumerable<CorrelationLimitationDto> limitations,
        OperationErrorDto? operationError = null)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        if (hwnd == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(hwnd), hwnd, "An HWND must be positive.");
        }

        Hwnd = hwnd;
        EvidenceFacts = ContractValidation.CopyRequired(evidenceFacts, nameof(evidenceFacts));
        Limitations = ContractValidation.CopyRequired(limitations, nameof(limitations));
        OperationError = operationError;
    }

    public ExternalUiaCaptureRefDto Source { get; }

    public ulong Hwnd { get; }

    public IReadOnlyList<CorrelationEvidenceFactDto> EvidenceFacts { get; }

    public IReadOnlyList<CorrelationLimitationDto> Limitations { get; }

    public OperationErrorDto? OperationError { get; }
}
