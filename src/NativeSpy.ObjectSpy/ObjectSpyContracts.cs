using System.Drawing;
using NativeSpy.Client.Correlation;
using NativeSpy.FlaUI;
using NativeSpy.Protocol.Clr;
using NativeSpy.Protocol.Common;
using NativeSpy.Protocol.Correlation;

namespace NativeSpy.ObjectSpy;

public enum ObjectSpySelectionState
{
    Idle,
    Previewing,
    PreviewReady,
    Frozen,
    Stale,
    Abandoned,
    Error,
    Quarantined
}

public enum ObjectSpyClrState
{
    Unavailable,
    Loading,
    Ready,
    Error,
    Collected
}

public sealed class ObjectSpyOverlayGeometry
{
    public ObjectSpyOverlayGeometry(Rectangle bounds)
    {
        Bounds = bounds;
    }

    public Rectangle Bounds { get; }
}

public interface IObjectSpyOverlay
{
    void ShowPreview(ObjectSpyOverlayGeometry geometry, long generation);

    void Clear(long generation);
}

public sealed class NullObjectSpyOverlay : IObjectSpyOverlay
{
    public void ShowPreview(ObjectSpyOverlayGeometry geometry, long generation)
    {
    }

    public void Clear(long generation)
    {
    }
}

internal interface IObjectSpyUiSession
{
    int ProcessId { get; }

    bool IsPoisoned { get; }

    Task<FlaUiSelectionObservation> PreviewFromPointAsync(
        Point screenPoint,
        CancellationToken cancellationToken);

    Task<ObjectSpyFrozenSelection> FreezeFromPointAsync(
        Point screenPoint,
        CancellationToken cancellationToken);
}

internal sealed class ObjectSpyFrozenSelection : IAsyncDisposable
{
    private readonly IAsyncDisposable? _lifetime;

    public ObjectSpyFrozenSelection(
        IExternalUiaObservationPort source,
        FlaUiSelectionObservation observation,
        IAsyncDisposable? lifetime = null)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Observation = observation ?? throw new ArgumentNullException(nameof(observation));
        _lifetime = lifetime;
    }

    public IExternalUiaObservationPort Source { get; }

    public FlaUiSelectionObservation Observation { get; }

    public ValueTask DisposeAsync()
    {
        return _lifetime?.DisposeAsync() ?? ValueTask.CompletedTask;
    }
}

public sealed class ObjectSpyViewState
{
    public ObjectSpySelectionState SelectionState { get; internal set; }

    public ObjectSpyClrState ClrState { get; internal set; }

    public long Generation { get; internal set; }

    public FlaUiSelectionObservation? SelectionObservation { get; internal set; }

    public FlaUiSelectionObservation? PreviewObservation { get; internal set; }

    public ExternalUiaEvidenceDto? ExternalEvidence { get; internal set; }

    public CorrelationResultDto? Correlation { get; internal set; }

    public ManagedObjectRefDto? ManagedObject { get; internal set; }

    public DescribeObjectResponseDto? Description { get; internal set; }

    public IReadOnlyList<MemberDescriptorDto> Members { get; internal set; } = Array.Empty<MemberDescriptorDto>();

    public string? NextContinuationToken { get; internal set; }

    public IReadOnlyList<MemberReadResultDto> FieldResults { get; internal set; } = Array.Empty<MemberReadResultDto>();

    public MemberReadResultDto? LastReadResult { get; internal set; }

    public string? Error { get; internal set; }

    public int NavigationDepth { get; internal set; }
}
