using System.Drawing;
using NativeSpy.Client.ClrInspection;
using NativeSpy.Client.Correlation;
using NativeSpy.FlaUI;
using NativeSpy.Protocol.Clr;
using NativeSpy.Protocol.Common;
using NativeSpy.Protocol.Correlation;

namespace NativeSpy.ObjectSpy;

public sealed class ObjectSpyCoordinator : IAsyncDisposable
{
    private readonly ProcessIdentityDto _targetProcessIdentity;
    private readonly FlaUiAutomationSession _flaUiSession;
    private readonly IWinFormsCorrelationPort _correlationPort;
    private readonly IClrInspectionPort _inspectionPort;
    private readonly IObjectSpyOverlay _overlay;
    private readonly CorrelationCoordinator _correlationCoordinator = new();
    private readonly CorrelationPolicyDto _policy = new(
        CorrelationPolicyMode.Conservative,
        maxCandidates: 1,
        maxExternalNodes: 1,
        maxProviderNodes: 1);
    private readonly object _gate = new();
    private readonly List<NavigationFrame> _navigation = new();
    private readonly ObjectSpyViewState _state = new();
    private FlaUiFrozenSelection? _frozenSelection;
    private int _disposed;

    public ObjectSpyCoordinator(
        ProcessIdentityDto targetProcessIdentity,
        FlaUiAutomationSession flaUiSession,
        IWinFormsCorrelationPort correlationPort,
        IClrInspectionPort inspectionPort,
        IObjectSpyOverlay? overlay = null)
    {
        _targetProcessIdentity = targetProcessIdentity ?? throw new ArgumentNullException(nameof(targetProcessIdentity));
        _flaUiSession = flaUiSession ?? throw new ArgumentNullException(nameof(flaUiSession));
        if (_flaUiSession.ProcessId != _targetProcessIdentity.ProcessId)
        {
            throw new ArgumentException("The UI Automation session is attached to a different process.", nameof(flaUiSession));
        }
        _correlationPort = correlationPort ?? throw new ArgumentNullException(nameof(correlationPort));
        _inspectionPort = inspectionPort ?? throw new ArgumentNullException(nameof(inspectionPort));
        _overlay = overlay ?? new NullObjectSpyOverlay();
        _state.SelectionState = ObjectSpySelectionState.Idle;
        _state.ClrState = ObjectSpyClrState.Unavailable;
    }

    public ObjectSpyViewState State => _state;

    public event EventHandler? StateChanged;

    public async Task PreviewAsync(Point screenPoint, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        var generation = BeginGeneration(ObjectSpySelectionState.Previewing, clearClr: false);
        try
        {
            var observation = await _flaUiSession
                .PreviewFromPointAsync(screenPoint, cancellationToken)
                .ConfigureAwait(false);
            if (!IsCurrent(generation))
            {
                return;
            }

            lock (_gate)
            {
                _state.SelectionObservation = observation;
                _state.Error = observation.IsAvailable ? null : observation.Limitation;
                _state.SelectionState = observation.IsAvailable
                    ? ObjectSpySelectionState.PreviewReady
                    : ObjectSpySelectionState.Error;
            }

            if (observation.Bounds is Rectangle bounds && observation.IsAvailable)
            {
                _overlay.ShowPreview(new ObjectSpyOverlayGeometry(bounds), generation);
            }
            else
            {
                _overlay.Clear(generation);
            }

            PublishStateChanged();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (IsCurrent(generation))
            {
                SetSelectionState(ObjectSpySelectionState.Abandoned, "Preview abandoned.");
            }
        }
        catch (FlaUiSessionException exception)
        {
            if (IsCurrent(generation))
            {
                SetSelectionState(
                    _flaUiSession.IsPoisoned
                        ? ObjectSpySelectionState.Quarantined
                        : ObjectSpySelectionState.Error,
                    exception.Message);
            }
        }
        catch (Exception exception) when (IsExpectedFlaUiFailure(exception))
        {
            if (IsCurrent(generation))
            {
                SetSelectionState(
                    _flaUiSession.IsPoisoned
                        ? ObjectSpySelectionState.Quarantined
                        : ObjectSpySelectionState.Error,
                    exception.Message);
            }
        }
    }

    public void AbandonPreview()
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        var generation = BeginGeneration(ObjectSpySelectionState.Abandoned, clearClr: false);
        _overlay.Clear(generation);
        PublishStateChanged();
    }

    public async Task FreezeAsync(Point screenPoint, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        var generation = BeginGeneration(ObjectSpySelectionState.Frozen, clearClr: true);
        _overlay.Clear(generation);
        try
        {
            var frozen = await _flaUiSession
                .FreezeFromPointAsync(screenPoint, cancellationToken)
                .ConfigureAwait(false);
            if (!IsCurrent(generation))
            {
                await frozen.DisposeAsync().ConfigureAwait(false);
                return;
            }

            var previousFrozen = _frozenSelection;
            _frozenSelection = frozen;
            if (previousFrozen is not null)
            {
                await previousFrozen.DisposeAsync().ConfigureAwait(false);
            }

            lock (_gate)
            {
                _state.SelectionObservation = frozen.Observation;
                _state.SelectionState = ObjectSpySelectionState.Frozen;
                _state.Error = null;
            }
            PublishStateChanged();

            var externalEvidence = await frozen.Source
                .CaptureAsync(cancellationToken)
                .ConfigureAwait(false);
            if (!IsCurrent(generation))
            {
                return;
            }

            lock (_gate)
            {
                _state.ExternalEvidence = externalEvidence;
            }
            PublishStateChanged();

            var correlation = await _correlationCoordinator
                .ResolveUiaToWinFormsAsync(
                    externalEvidence,
                    frozen.Source,
                    _correlationPort,
                    _policy,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!IsCurrent(generation))
            {
                return;
            }

            lock (_gate)
            {
                _state.Correlation = correlation;
            }
            PublishStateChanged();

            if (_flaUiSession.IsPoisoned)
            {
                SetSelectionState(ObjectSpySelectionState.Quarantined, "The UI Automation session was quarantined after a timeout.");
                return;
            }

            if (correlation.OperationError is not null)
            {
                ApplyInspectionError(correlation.OperationError);
                return;
            }

            if (correlation.Status != CorrelationStatus.Exact
                || correlation.PrimaryCandidateId is null
                || correlation.Candidates.Count != 1
                || correlation.Candidates[0].Target.TargetKind != CorrelationTargetKind.ManagedObject
                || correlation.Candidates[0].Target.Managed is null)
            {
                lock (_gate)
                {
                    _state.ClrState = ObjectSpyClrState.Unavailable;
                    _state.ManagedObject = null;
                    _state.Description = null;
                    _state.Members = Array.Empty<MemberDescriptorDto>();
                    _state.NextContinuationToken = null;
                    _state.FieldResults = Array.Empty<MemberReadResultDto>();
                    _state.Error = null;
                }
                PublishStateChanged();
                return;
            }

            var managedObject = correlation.Candidates[0].Target.Managed!;
            await LoadClrRootAsync(
                    generation,
                    managedObject,
                    resetNavigation: true,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (IsCurrent(generation))
            {
                SetSelectionState(ObjectSpySelectionState.Abandoned, "Freeze abandoned.");
            }
        }
        catch (FlaUiSessionException exception)
        {
            if (IsCurrent(generation))
            {
                SetSelectionState(
                    _flaUiSession.IsPoisoned
                        ? ObjectSpySelectionState.Quarantined
                        : ObjectSpySelectionState.Error,
                    exception.Message);
            }
        }
        catch (Exception exception) when (IsExpectedFlaUiFailure(exception))
        {
            if (IsCurrent(generation))
            {
                SetSelectionState(
                    _flaUiSession.IsPoisoned
                        ? ObjectSpySelectionState.Quarantined
                        : ObjectSpySelectionState.Error,
                    exception.Message);
            }
        }
    }

    public async Task ReadPropertyAsync(
        MemberDescriptorDto property,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        if (property is null)
        {
            throw new ArgumentNullException(nameof(property));
        }

        ManagedObjectRefDto? @object;
        long generation;
        lock (_gate)
        {
            generation = _state.Generation;
            @object = _state.ManagedObject;
            if (_state.ClrState != ObjectSpyClrState.Ready || property.Kind != ClrMemberKind.Property)
            {
                _state.Error = "The property is not currently readable.";
                PublishStateChanged();
                return;
            }
        }

        var result = await _inspectionPort
            .ReadPropertyValueAsync(@object!, property.Member, cancellationToken)
            .ConfigureAwait(false);
        if (!IsCurrent(generation))
        {
            return;
        }

        if (!result.IsSuccess)
        {
            ApplyInspectionError(result.Error!);
            return;
        }

        lock (_gate)
        {
            _state.LastReadResult = result.Value!.Result;
            _state.Error = null;
        }
        PublishStateChanged();
    }

    public async Task FollowObjectReferenceAsync(
        ClrValueDto value,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        if (value.Kind != ClrValueKind.ObjectReference || value.ObjectReference is null)
        {
            throw new ArgumentException("Only an ObjectReference value can be followed.", nameof(value));
        }

        ManagedObjectRefDto? current;
        lock (_gate)
        {
            current = _state.ManagedObject;
            if (current is null || _state.ClrState != ObjectSpyClrState.Ready)
            {
                return;
            }

            _navigation.Add(new NavigationFrame(
                current,
                _state.Description,
                _state.Members,
                _state.NextContinuationToken,
                _state.FieldResults,
                _state.LastReadResult));
            _state.NavigationDepth = _navigation.Count;
        }
        PublishStateChanged();

        await LoadClrRootAsync(
                CurrentGeneration,
                value.ObjectReference,
                resetNavigation: false,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task LoadNextMemberPageAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        ManagedObjectRefDto? @object;
        string? continuation;
        long generation;
        lock (_gate)
        {
            generation = _state.Generation;
            @object = _state.ManagedObject;
            continuation = _state.NextContinuationToken;
            if (_state.ClrState != ObjectSpyClrState.Ready || @object is null || continuation is null)
            {
                return;
            }
        }

        var response = await _inspectionPort
            .ListMembersAsync(@object, 128, ClrMemberKindFilter.All, continuation, cancellationToken)
            .ConfigureAwait(false);
        if (!IsCurrent(generation))
        {
            return;
        }

        if (!response.IsSuccess)
        {
            ApplyInspectionError(response.Error!);
            return;
        }

        var page = response.Value!.Members;
        var fields = page
            .Where(member => member.Kind == ClrMemberKind.Field)
            .Select(member => member.Member)
            .ToArray();
        var results = new List<MemberReadResultDto>(fields.Length);
        foreach (var batch in fields.Chunk(64))
        {
            var fieldResponse = await _inspectionPort
                .ReadFieldValuesAsync(@object, batch, cancellationToken)
                .ConfigureAwait(false);
            if (!IsCurrent(generation))
            {
                return;
            }

            if (!fieldResponse.IsSuccess)
            {
                ApplyInspectionError(fieldResponse.Error!);
                return;
            }

            results.AddRange(fieldResponse.Value!.Results);
        }

        lock (_gate)
        {
            _state.Members = _state.Members.Concat(page).ToArray();
            _state.FieldResults = _state.FieldResults.Concat(results).ToArray();
            _state.NextContinuationToken = response.Value.NextContinuationToken;
            _state.Error = null;
        }
        PublishStateChanged();
    }

    public void Back()
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        NavigationFrame? frame;
        lock (_gate)
        {
            if (_navigation.Count == 0)
            {
                return;
            }

            frame = _navigation[^1];
            _navigation.RemoveAt(_navigation.Count - 1);
            _state.ManagedObject = frame.Object;
            _state.Description = frame.Description;
            _state.Members = frame.Members;
            _state.NextContinuationToken = frame.NextContinuationToken;
            _state.FieldResults = frame.FieldResults;
            _state.LastReadResult = frame.LastReadResult;
            _state.ClrState = ObjectSpyClrState.Ready;
            _state.NavigationDepth = _navigation.Count;
            _state.Error = null;
        }
        PublishStateChanged();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _overlay.Clear(CurrentGeneration);
        if (_frozenSelection is not null)
        {
            await _frozenSelection.DisposeAsync().ConfigureAwait(false);
            _frozenSelection = null;
        }
    }

    private async Task LoadClrRootAsync(
        long generation,
        ManagedObjectRefDto @object,
        bool resetNavigation,
        CancellationToken cancellationToken)
    {
        if (!IsCurrent(generation))
        {
            return;
        }

        lock (_gate)
        {
            _state.ClrState = ObjectSpyClrState.Loading;
            _state.ManagedObject = @object;
            _state.LastReadResult = null;
            if (resetNavigation)
            {
                _navigation.Clear();
                _state.NavigationDepth = 0;
            }
        }
        PublishStateChanged();

        var description = await _inspectionPort
            .DescribeObjectAsync(@object, cancellationToken)
            .ConfigureAwait(false);
        if (!IsCurrent(generation))
        {
            return;
        }

        if (!description.IsSuccess)
        {
            ApplyInspectionError(description.Error!);
            return;
        }

        var members = await _inspectionPort
            .ListMembersAsync(
                description.Value!.Object,
                128,
                ClrMemberKindFilter.All,
                continuationToken: null,
                cancellationToken)
            .ConfigureAwait(false);
        if (!IsCurrent(generation))
        {
            return;
        }

        if (!members.IsSuccess)
        {
            ApplyInspectionError(members.Error!);
            return;
        }

        var fields = members.Value!.Members
            .Where(member => member.Kind == ClrMemberKind.Field)
            .Select(member => member.Member)
            .ToArray();
        var fieldResults = new List<MemberReadResultDto>(fields.Length);
        foreach (var batch in fields.Chunk(64))
        {
            var fieldResponse = await _inspectionPort
                .ReadFieldValuesAsync(description.Value.Object, batch, cancellationToken)
                .ConfigureAwait(false);
            if (!IsCurrent(generation))
            {
                return;
            }

            if (!fieldResponse.IsSuccess)
            {
                ApplyInspectionError(fieldResponse.Error!);
                return;
            }

            fieldResults.AddRange(fieldResponse.Value!.Results);
        }

        lock (_gate)
        {
            _state.ClrState = ObjectSpyClrState.Ready;
            _state.Description = description.Value;
            _state.Members = members.Value.Members;
            _state.NextContinuationToken = members.Value.NextContinuationToken;
            _state.FieldResults = fieldResults;
            _state.Error = null;
        }
        PublishStateChanged();
    }

    private long BeginGeneration(ObjectSpySelectionState state, bool clearClr)
    {
        lock (_gate)
        {
            _state.Generation++;
            _state.SelectionState = state;
            _state.Error = null;
            if (clearClr)
            {
                _navigation.Clear();
                _state.ClrState = ObjectSpyClrState.Unavailable;
                _state.ManagedObject = null;
                _state.Description = null;
                _state.Members = Array.Empty<MemberDescriptorDto>();
                _state.NextContinuationToken = null;
                _state.FieldResults = Array.Empty<MemberReadResultDto>();
                _state.LastReadResult = null;
                _state.NavigationDepth = 0;
                _state.Correlation = null;
                _state.ExternalEvidence = null;
            }

            return _state.Generation;
        }
    }

    private long CurrentGeneration
    {
        get
        {
            lock (_gate)
            {
                return _state.Generation;
            }
        }
    }

    private bool IsCurrent(long generation)
    {
        return CurrentGeneration == generation;
    }

    private void SetSelectionState(ObjectSpySelectionState state, string? error)
    {
        lock (_gate)
        {
            _state.SelectionState = state;
            _state.Error = error;
        }
        PublishStateChanged();
    }

    private void ApplyInspectionError(OperationErrorDto error)
    {
        lock (_gate)
        {
            _state.SelectionState = error.Code == OperationErrorCode.StaleHandle
                ? ObjectSpySelectionState.Stale
                : _state.SelectionState;
            _state.ClrState = error.Code == OperationErrorCode.ObjectCollected
                ? ObjectSpyClrState.Collected
                : ObjectSpyClrState.Error;
            _state.Error = error.Message ?? error.Code.ToString();
        }
        PublishStateChanged();
    }

    private void PublishStateChanged()
    {
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private static bool IsExpectedFlaUiFailure(Exception exception)
    {
        return exception is InvalidOperationException
            or TimeoutException
            or ArgumentException
            or System.Runtime.InteropServices.COMException;
    }

    private sealed record NavigationFrame(
        ManagedObjectRefDto Object,
        DescribeObjectResponseDto? Description,
        IReadOnlyList<MemberDescriptorDto> Members,
        string? NextContinuationToken,
        IReadOnlyList<MemberReadResultDto> FieldResults,
        MemberReadResultDto? LastReadResult);
}
