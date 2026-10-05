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
    private readonly IObjectSpyUiSession _uiSession;
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
    private ObjectSpyFrozenSelection? _frozenSelection;
    private long _externalOperationEpoch;
    private long _previewEpoch;
    private long _overlayGeneration;
    private long _clrNavigationGeneration;
    private int _disposed;

    public ObjectSpyCoordinator(
        ProcessIdentityDto targetProcessIdentity,
        FlaUiAutomationSession flaUiSession,
        IWinFormsCorrelationPort correlationPort,
        IClrInspectionPort inspectionPort,
        IObjectSpyOverlay? overlay = null)
        : this(
            targetProcessIdentity,
            CreateFlaUiSessionAdapter(flaUiSession),
            correlationPort,
            inspectionPort,
            overlay)
    {
    }

    internal ObjectSpyCoordinator(
        ProcessIdentityDto targetProcessIdentity,
        IObjectSpyUiSession uiSession,
        IWinFormsCorrelationPort correlationPort,
        IClrInspectionPort inspectionPort,
        IObjectSpyOverlay? overlay = null)
    {
        _targetProcessIdentity = targetProcessIdentity ?? throw new ArgumentNullException(nameof(targetProcessIdentity));
        _uiSession = uiSession ?? throw new ArgumentNullException(nameof(uiSession));
        if (_uiSession.ProcessId != _targetProcessIdentity.ProcessId)
        {
            throw new ArgumentException("The UI Automation session is attached to a different process.", nameof(uiSession));
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
        var stamp = BeginPreview();
        try
        {
            var observation = await _uiSession
                .PreviewFromPointAsync(screenPoint, cancellationToken)
                .ConfigureAwait(false);
            if (!TryCommitPreview(stamp, observation))
            {
                return;
            }

            if (observation.Bounds is Rectangle bounds && observation.IsAvailable)
            {
                _overlay.ShowPreview(new ObjectSpyOverlayGeometry(bounds), stamp.OverlayGeneration);
            }
            else
            {
                _overlay.Clear(stamp.OverlayGeneration);
            }

            PublishStateChanged();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SetPreviewState(stamp, ObjectSpySelectionState.Abandoned, "Preview abandoned.");
        }
        catch (FlaUiSessionException exception)
        {
            SetPreviewState(
                stamp,
                _uiSession.IsPoisoned
                    ? ObjectSpySelectionState.Quarantined
                    : ObjectSpySelectionState.Error,
                exception.Message);
        }
        catch (Exception exception) when (IsExpectedFlaUiFailure(exception))
        {
            SetPreviewState(
                stamp,
                _uiSession.IsPoisoned
                    ? ObjectSpySelectionState.Quarantined
                    : ObjectSpySelectionState.Error,
                exception.Message);
        }
    }

    public void AbandonPreview()
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        var overlayGeneration = BeginAbandonPreview();
        _overlay.Clear(overlayGeneration);
        PublishStateChanged();
    }

    public async Task FreezeAsync(Point screenPoint, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        var attempt = BeginFreezeAttempt();
        _overlay.Clear(attempt.OverlayGeneration);
        ObjectSpyFrozenSelection? candidate = null;
        try
        {
            candidate = await _uiSession
                .FreezeFromPointAsync(screenPoint, cancellationToken)
                .ConfigureAwait(false);
            var commit = TryCommitFrozenSelection(attempt, candidate);
            if (commit is null)
            {
                return;
            }

            candidate = null;
            var committed = commit.Value;
            if (committed.PreviousSelection is not null)
            {
                await committed.PreviousSelection.DisposeAsync().ConfigureAwait(false);
            }

            PublishStateChanged();
            var frozen = committed.Selection;
            var externalEvidence = await frozen.Source
                .CaptureAsync(cancellationToken)
                .ConfigureAwait(false);
            if (!TryCommitExternalEvidence(attempt, externalEvidence))
            {
                return;
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
            if (!TryCommitCorrelation(attempt, correlation))
            {
                return;
            }
            PublishStateChanged();

            if (_uiSession.IsPoisoned)
            {
                SetFreezeState(
                    attempt,
                    ObjectSpySelectionState.Quarantined,
                    "The UI Automation session was quarantined after a timeout.");
                return;
            }

            if (IsStaleCorrelation(correlation))
            {
                SetFreezeState(
                    attempt,
                    ObjectSpySelectionState.Stale,
                    "The frozen UI Automation source is no longer valid.");
                return;
            }

            if (correlation.OperationError is not null)
            {
                SetFreezeInspectionError(attempt, correlation.OperationError);
                return;
            }

            if (correlation.Status != CorrelationStatus.Exact
                || correlation.PrimaryCandidateId is null
                || correlation.Candidates.Count != 1
                || correlation.Candidates[0].Target.TargetKind != CorrelationTargetKind.ManagedObject
                || correlation.Candidates[0].Target.Managed is null)
            {
                if (TrySetClrUnavailable(attempt))
                {
                    PublishStateChanged();
                }
                return;
            }

            await LoadClrRootAsync(
                    committed.SelectionGeneration,
                    committed.ClrNavigationGeneration,
                    correlation.Candidates[0].Target.Managed!,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SetFreezeState(attempt, ObjectSpySelectionState.Abandoned, "Freeze abandoned.");
        }
        catch (FlaUiSessionException exception)
        {
            SetFreezeState(
                attempt,
                _uiSession.IsPoisoned
                    ? ObjectSpySelectionState.Quarantined
                    : ObjectSpySelectionState.Error,
                exception.Message);
        }
        catch (Exception exception) when (IsExpectedFlaUiFailure(exception))
        {
            SetFreezeState(
                attempt,
                _uiSession.IsPoisoned
                    ? ObjectSpySelectionState.Quarantined
                    : ObjectSpySelectionState.Error,
                exception.Message);
        }
        finally
        {
            if (candidate is not null)
            {
                await candidate.DisposeAsync().ConfigureAwait(false);
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
        ClrOperationStamp stamp;
        var canRead = true;
        lock (_gate)
        {
            @object = _state.ManagedObject;
            stamp = new ClrOperationStamp(_state.Generation, _clrNavigationGeneration);
            if (_state.ClrState != ObjectSpyClrState.Ready || property.Kind != ClrMemberKind.Property)
            {
                _state.Error = "The property is not currently readable.";
                canRead = false;
            }
        }

        if (!canRead)
        {
            PublishStateChanged();
            return;
        }

        var result = await _inspectionPort
            .ReadPropertyValueAsync(@object!, property.Member, cancellationToken)
            .ConfigureAwait(false);
        if (!IsClrCurrent(stamp))
        {
            return;
        }

        if (!result.IsSuccess)
        {
            SetClrInspectionError(stamp, result.Error!);
            return;
        }

        lock (_gate)
        {
            if (!IsClrCurrentLocked(stamp))
            {
                return;
            }

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

        ManagedObjectRefDto current;
        NavigationFrame frame;
        ClrOperationStamp stamp;
        lock (_gate)
        {
            if (_state.ManagedObject is null || _state.ClrState != ObjectSpyClrState.Ready)
            {
                return;
            }

            current = _state.ManagedObject;
            frame = new NavigationFrame(
                current,
                _state.Description,
                _state.Members,
                _state.NextContinuationToken,
                _state.FieldResults,
                _state.LastReadResult);
            stamp = new ClrOperationStamp(_state.Generation, ++_clrNavigationGeneration);
            _state.ClrState = ObjectSpyClrState.Loading;
            _state.Error = null;
        }
        PublishStateChanged();

        try
        {
            var loaded = await LoadClrSnapshotAsync(
                    stamp,
                    value.ObjectReference,
                    cancellationToken)
                .ConfigureAwait(false);
            if (loaded is null)
            {
                return;
            }

            if (!loaded.IsSuccess)
            {
                RestoreFailedNavigation(stamp, loaded.Error?.Message ?? "The followed object could not be loaded.");
                return;
            }

            lock (_gate)
            {
                if (!IsClrCurrentLocked(stamp))
                {
                    return;
                }

                _navigation.Add(frame);
                ApplyLoadedSnapshotLocked(value.ObjectReference, loaded);
                _state.NavigationDepth = _navigation.Count;
                _state.ClrState = ObjectSpyClrState.Ready;
                _state.Error = null;
            }
            PublishStateChanged();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            RestoreFailedNavigation(stamp, "Object navigation abandoned.");
        }
    }

    public async Task LoadNextMemberPageAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        ManagedObjectRefDto? @object;
        string? continuation;
        ClrOperationStamp stamp;
        lock (_gate)
        {
            stamp = new ClrOperationStamp(_state.Generation, _clrNavigationGeneration);
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
        if (!IsClrCurrent(stamp))
        {
            return;
        }

        if (!response.IsSuccess)
        {
            SetClrInspectionError(stamp, response.Error!);
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
            if (!IsClrCurrent(stamp))
            {
                return;
            }

            if (!fieldResponse.IsSuccess)
            {
                SetClrInspectionError(stamp, fieldResponse.Error!);
                return;
            }

            results.AddRange(fieldResponse.Value!.Results);
        }

        lock (_gate)
        {
            if (!IsClrCurrentLocked(stamp))
            {
                return;
            }

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
        var changed = false;
        lock (_gate)
        {
            ++_clrNavigationGeneration;
            if (_navigation.Count == 0)
            {
                if (_state.ClrState == ObjectSpyClrState.Loading && _state.Description is not null)
                {
                    _state.ClrState = ObjectSpyClrState.Ready;
                    _state.Error = null;
                    changed = true;
                }
            }
            else
            {
                var frame = _navigation[^1];
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
                changed = true;
            }
        }

        if (changed)
        {
            PublishStateChanged();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        ObjectSpyFrozenSelection? frozen;
        long overlayGeneration;
        lock (_gate)
        {
            ++_externalOperationEpoch;
            ++_previewEpoch;
            ++_clrNavigationGeneration;
            overlayGeneration = ++_overlayGeneration;
            frozen = _frozenSelection;
            _frozenSelection = null;
        }

        _overlay.Clear(overlayGeneration);
        if (frozen is not null)
        {
            await frozen.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task LoadClrRootAsync(
        long selectionGeneration,
        long navigationGeneration,
        ManagedObjectRefDto @object,
        CancellationToken cancellationToken)
    {
        var stamp = new ClrOperationStamp(selectionGeneration, navigationGeneration);
        if (!IsClrCurrent(stamp))
        {
            return;
        }

        lock (_gate)
        {
            if (!IsClrCurrentLocked(stamp))
            {
                return;
            }

            _state.ClrState = ObjectSpyClrState.Loading;
            _state.ManagedObject = @object;
            _state.LastReadResult = null;
        }
        PublishStateChanged();

        var loaded = await LoadClrSnapshotAsync(stamp, @object, cancellationToken).ConfigureAwait(false);
        if (loaded is null)
        {
            return;
        }

        if (!loaded.IsSuccess)
        {
            SetClrInspectionError(stamp, loaded.Error!);
            return;
        }

        lock (_gate)
        {
            if (!IsClrCurrentLocked(stamp))
            {
                return;
            }

            ApplyLoadedSnapshotLocked(@object, loaded);
            _state.ClrState = ObjectSpyClrState.Ready;
            _state.Error = null;
        }
        PublishStateChanged();
    }

    private async Task<ClrLoadSnapshot?> LoadClrSnapshotAsync(
        ClrOperationStamp stamp,
        ManagedObjectRefDto @object,
        CancellationToken cancellationToken)
    {
        if (!IsClrCurrent(stamp))
        {
            return null;
        }

        var description = await _inspectionPort
            .DescribeObjectAsync(@object, cancellationToken)
            .ConfigureAwait(false);
        if (!IsClrCurrent(stamp))
        {
            return null;
        }

        if (!description.IsSuccess)
        {
            return ClrLoadSnapshot.Failure(description.Error!);
        }

        var members = await _inspectionPort
            .ListMembersAsync(
                description.Value!.Object,
                128,
                ClrMemberKindFilter.All,
                continuationToken: null,
                cancellationToken)
            .ConfigureAwait(false);
        if (!IsClrCurrent(stamp))
        {
            return null;
        }

        if (!members.IsSuccess)
        {
            return ClrLoadSnapshot.Failure(members.Error!);
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
            if (!IsClrCurrent(stamp))
            {
                return null;
            }

            if (!fieldResponse.IsSuccess)
            {
                return ClrLoadSnapshot.Failure(fieldResponse.Error!);
            }

            fieldResults.AddRange(fieldResponse.Value!.Results);
        }

        return ClrLoadSnapshot.Success(
            description.Value,
            members.Value.Members,
            members.Value.NextContinuationToken,
            fieldResults);
    }

    private PreviewStamp BeginPreview()
    {
        lock (_gate)
        {
            var externalEpoch = ++_externalOperationEpoch;
            var previewEpoch = ++_previewEpoch;
            var overlayGeneration = ++_overlayGeneration;
            _state.SelectionState = ObjectSpySelectionState.Previewing;
            _state.PreviewObservation = null;
            _state.Error = null;
            return new PreviewStamp(externalEpoch, previewEpoch, overlayGeneration);
        }
    }

    private long BeginAbandonPreview()
    {
        lock (_gate)
        {
            ++_externalOperationEpoch;
            ++_previewEpoch;
            _state.PreviewObservation = null;
            _state.SelectionState = _frozenSelection is null
                ? ObjectSpySelectionState.Abandoned
                : ObjectSpySelectionState.Frozen;
            _state.Error = null;
            return ++_overlayGeneration;
        }
    }

    private FreezeStamp BeginFreezeAttempt()
    {
        lock (_gate)
        {
            var externalEpoch = ++_externalOperationEpoch;
            ++_previewEpoch;
            ++_clrNavigationGeneration;
            var overlayGeneration = ++_overlayGeneration;
            _state.PreviewObservation = null;
            _state.Error = null;
            return new FreezeStamp(externalEpoch, overlayGeneration);
        }
    }

    private FreezeCommit? TryCommitFrozenSelection(
        FreezeStamp stamp,
        ObjectSpyFrozenSelection frozen)
    {
        lock (_gate)
        {
            if (_externalOperationEpoch != stamp.ExternalEpoch)
            {
                return null;
            }

            var previous = _frozenSelection;
            _frozenSelection = frozen;
            _state.Generation++;
            ++_clrNavigationGeneration;
            ResetClrStateLocked();
            _state.SelectionState = ObjectSpySelectionState.Frozen;
            _state.SelectionObservation = frozen.Observation;
            _state.PreviewObservation = null;
            _state.ExternalEvidence = null;
            _state.Correlation = null;
            _state.Error = null;
            return new FreezeCommit(frozen, previous, _state.Generation, _clrNavigationGeneration);
        }
    }

    private void ResetClrStateLocked()
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
    }

    private bool TryCommitPreview(
        PreviewStamp stamp,
        FlaUiSelectionObservation observation)
    {
        lock (_gate)
        {
            if (_externalOperationEpoch != stamp.ExternalEpoch
                || _previewEpoch != stamp.PreviewEpoch)
            {
                return false;
            }

            _state.PreviewObservation = observation;
            _state.Error = observation.IsAvailable ? null : observation.Limitation;
            _state.SelectionState = observation.IsAvailable
                ? ObjectSpySelectionState.PreviewReady
                : ObjectSpySelectionState.Error;
            return true;
        }
    }

    private void SetPreviewState(
        PreviewStamp stamp,
        ObjectSpySelectionState state,
        string? error)
    {
        lock (_gate)
        {
            if (_externalOperationEpoch != stamp.ExternalEpoch
                || _previewEpoch != stamp.PreviewEpoch)
            {
                return;
            }

            _state.SelectionState = state;
            _state.Error = error;
        }
        PublishStateChanged();
    }

    private bool IsClrCurrent(ClrOperationStamp stamp)
    {
        lock (_gate)
        {
            return IsClrCurrentLocked(stamp);
        }
    }

    private bool IsClrCurrentLocked(ClrOperationStamp stamp)
    {
        return _state.Generation == stamp.SelectionGeneration
            && _clrNavigationGeneration == stamp.NavigationGeneration;
    }

    private void RestoreFailedNavigation(ClrOperationStamp stamp, string error)
    {
        lock (_gate)
        {
            if (!IsClrCurrentLocked(stamp))
            {
                return;
            }

            _state.ClrState = ObjectSpyClrState.Ready;
            _state.Error = error;
        }
        PublishStateChanged();
    }

    private void ApplyLoadedSnapshotLocked(
        ManagedObjectRefDto @object,
        ClrLoadSnapshot snapshot)
    {
        _state.ManagedObject = @object;
        _state.Description = snapshot.Description;
        _state.Members = snapshot.Members;
        _state.NextContinuationToken = snapshot.NextContinuationToken;
        _state.FieldResults = snapshot.FieldResults;
    }

    private void SetFreezeState(
        FreezeStamp stamp,
        ObjectSpySelectionState state,
        string? error)
    {
        lock (_gate)
        {
            if (_externalOperationEpoch != stamp.ExternalEpoch)
            {
                return;
            }

            _state.SelectionState = state;
            _state.Error = error;
        }
        PublishStateChanged();
    }

    private bool TryCommitExternalEvidence(
        FreezeStamp stamp,
        ExternalUiaEvidenceDto evidence)
    {
        lock (_gate)
        {
            if (_externalOperationEpoch != stamp.ExternalEpoch)
            {
                return false;
            }

            _state.ExternalEvidence = evidence;
            return true;
        }
    }

    private bool TryCommitCorrelation(
        FreezeStamp stamp,
        CorrelationResultDto correlation)
    {
        lock (_gate)
        {
            if (_externalOperationEpoch != stamp.ExternalEpoch)
            {
                return false;
            }

            _state.Correlation = correlation;
            return true;
        }
    }

    private bool TrySetClrUnavailable(FreezeStamp stamp)
    {
        lock (_gate)
        {
            if (_externalOperationEpoch != stamp.ExternalEpoch)
            {
                return false;
            }

            _state.ClrState = ObjectSpyClrState.Unavailable;
            _state.ManagedObject = null;
            _state.Description = null;
            _state.Members = Array.Empty<MemberDescriptorDto>();
            _state.NextContinuationToken = null;
            _state.FieldResults = Array.Empty<MemberReadResultDto>();
            _state.LastReadResult = null;
            _state.Error = null;
            return true;
        }
    }

    private void SetFreezeInspectionError(FreezeStamp stamp, OperationErrorDto error)
    {
        lock (_gate)
        {
            if (_externalOperationEpoch != stamp.ExternalEpoch)
            {
                return;
            }

            ApplyInspectionErrorLocked(error);
        }
        PublishStateChanged();
    }

    private void SetClrInspectionError(ClrOperationStamp stamp, OperationErrorDto error)
    {
        lock (_gate)
        {
            if (!IsClrCurrentLocked(stamp))
            {
                return;
            }

            ApplyInspectionErrorLocked(error);
        }
        PublishStateChanged();
    }

    private void ApplyInspectionErrorLocked(OperationErrorDto error)
    {
        _state.SelectionState = error.Code == OperationErrorCode.StaleHandle
            ? ObjectSpySelectionState.Stale
            : _state.SelectionState;
        _state.ClrState = error.Code == OperationErrorCode.ObjectCollected
            ? ObjectSpyClrState.Collected
            : ObjectSpyClrState.Error;
        _state.Error = error.Message ?? error.Code.ToString();
    }

    private void PublishStateChanged()
    {
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private static bool IsStaleCorrelation(CorrelationResultDto correlation)
    {
        return correlation.Limitations.Any(limitation => limitation.Code is
            "ExternalSourceNotAvailable"
            or "ExternalSourceUnavailableOrIncomplete"
            or "ExternalEvidenceSourceMismatch");
    }

    private static bool IsExpectedFlaUiFailure(Exception exception)
    {
        return exception is InvalidOperationException
            or TimeoutException
            or ArgumentException
            or System.Runtime.InteropServices.COMException;
    }

    private static IObjectSpyUiSession CreateFlaUiSessionAdapter(FlaUiAutomationSession session)
    {
        return new FlaUiSessionAdapter(session ?? throw new ArgumentNullException(nameof(session)));
    }

    private sealed class FlaUiSessionAdapter : IObjectSpyUiSession
    {
        private readonly FlaUiAutomationSession _session;

        public FlaUiSessionAdapter(FlaUiAutomationSession session)
        {
            _session = session;
        }

        public int ProcessId => _session.ProcessId;

        public bool IsPoisoned => _session.IsPoisoned;

        public Task<FlaUiSelectionObservation> PreviewFromPointAsync(
            Point screenPoint,
            CancellationToken cancellationToken)
        {
            return _session.PreviewFromPointAsync(screenPoint, cancellationToken);
        }

        public async Task<ObjectSpyFrozenSelection> FreezeFromPointAsync(
            Point screenPoint,
            CancellationToken cancellationToken)
        {
            var frozen = await _session
                .FreezeFromPointAsync(screenPoint, cancellationToken)
                .ConfigureAwait(false);
            return new ObjectSpyFrozenSelection(frozen.Source, frozen.Observation, frozen);
        }
    }

    private readonly record struct PreviewStamp(
        long ExternalEpoch,
        long PreviewEpoch,
        long OverlayGeneration);

    private readonly record struct FreezeStamp(long ExternalEpoch, long OverlayGeneration);

    private readonly record struct FreezeCommit(
        ObjectSpyFrozenSelection Selection,
        ObjectSpyFrozenSelection? PreviousSelection,
        long SelectionGeneration,
        long ClrNavigationGeneration);

    private readonly record struct ClrOperationStamp(
        long SelectionGeneration,
        long NavigationGeneration);

    private sealed record NavigationFrame(
        ManagedObjectRefDto Object,
        DescribeObjectResponseDto? Description,
        IReadOnlyList<MemberDescriptorDto> Members,
        string? NextContinuationToken,
        IReadOnlyList<MemberReadResultDto> FieldResults,
        MemberReadResultDto? LastReadResult);

    private sealed class ClrLoadSnapshot
    {
        private ClrLoadSnapshot(
            DescribeObjectResponseDto? description,
            IReadOnlyList<MemberDescriptorDto> members,
            string? nextContinuationToken,
            IReadOnlyList<MemberReadResultDto> fieldResults,
            OperationErrorDto? error)
        {
            Description = description;
            Members = members;
            NextContinuationToken = nextContinuationToken;
            FieldResults = fieldResults;
            Error = error;
        }

        public DescribeObjectResponseDto? Description { get; }

        public IReadOnlyList<MemberDescriptorDto> Members { get; }

        public string? NextContinuationToken { get; }

        public IReadOnlyList<MemberReadResultDto> FieldResults { get; }

        public OperationErrorDto? Error { get; }

        public bool IsSuccess => Error is null;

        public static ClrLoadSnapshot Success(
            DescribeObjectResponseDto description,
            IReadOnlyList<MemberDescriptorDto> members,
            string? nextContinuationToken,
            IReadOnlyList<MemberReadResultDto> fieldResults)
        {
            return new ClrLoadSnapshot(
                description,
                members,
                nextContinuationToken,
                fieldResults,
                error: null);
        }

        public static ClrLoadSnapshot Failure(OperationErrorDto error)
        {
            return new ClrLoadSnapshot(
                description: null,
                Array.Empty<MemberDescriptorDto>(),
                nextContinuationToken: null,
                Array.Empty<MemberReadResultDto>(),
                error ?? throw new ArgumentNullException(nameof(error)));
        }
    }
}
