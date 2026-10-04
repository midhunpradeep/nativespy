using NativeSpy.Client.Correlation;
using NativeSpy.Protocol.Common;
using NativeSpy.Protocol.Correlation;

namespace NativeSpy.IntegrationTests;

internal sealed class TestTargetWinFormsPort : IWinFormsCorrelationPort
{
    private readonly TestTargetProcess _target;

    public TestTargetWinFormsPort(TestTargetProcess target)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
    }

    public Task<FrameworkCorrelationEvidenceDto> BeginCurrentHwndAsync(
        ulong hwnd,
        CancellationToken cancellationToken)
    {
        return _target.Session.BeginCurrentHwndAsync(hwnd, cancellationToken);
    }

    public Task<FrameworkCorrelationEvidenceDto> RevalidateCurrentHwndAsync(
        ulong hwnd,
        HandleRefDto candidateHandle,
        CancellationToken cancellationToken)
    {
        return _target.Session.RevalidateCurrentHwndAsync(hwnd, candidateHandle, cancellationToken);
    }
}
