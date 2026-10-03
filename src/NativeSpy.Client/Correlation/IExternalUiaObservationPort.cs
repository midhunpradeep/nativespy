using NativeSpy.Protocol.Correlation;

namespace NativeSpy.Client.Correlation;

/// <summary>
/// Client-side boundary for one retained external UI Automation source.
/// </summary>
public interface IExternalUiaObservationPort
{
    Task<ExternalUiaEvidenceDto> CaptureAsync(CancellationToken cancellationToken);

    Task<ExternalUiaEqualityEvidenceDto> CompareWithHwndAsync(
        ulong hwnd,
        CancellationToken cancellationToken);
}
