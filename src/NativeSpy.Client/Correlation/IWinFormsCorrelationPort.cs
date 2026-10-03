using NativeSpy.Protocol.Common;
using NativeSpy.Protocol.Correlation;

namespace NativeSpy.Client.Correlation;

/// <summary>
/// Client-side boundary for detached target-side WinForms evidence.
/// </summary>
public interface IWinFormsCorrelationPort
{
    Task<FrameworkCorrelationEvidenceDto> BeginCurrentHwndAsync(
        ulong hwnd,
        CancellationToken cancellationToken);

    Task<FrameworkCorrelationEvidenceDto> RevalidateCurrentHwndAsync(
        ulong hwnd,
        HandleRefDto candidateHandle,
        CancellationToken cancellationToken);
}
