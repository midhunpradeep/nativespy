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
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_target.Begin(hwnd));
    }

    public Task<FrameworkCorrelationEvidenceDto> RevalidateCurrentHwndAsync(
        ulong hwnd,
        HandleRefDto candidateHandle,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_target.Revalidate(hwnd, candidateHandle));
    }
}
