using System.Drawing;
using NativeSpy.Client.ClrInspection;
using NativeSpy.Client.Correlation;
using NativeSpy.FlaUI;
using NativeSpy.ObjectSpy;
using NativeSpy.Protocol.Clr;
using NativeSpy.Protocol.Common;
using NativeSpy.Protocol.Correlation;
using Xunit;

namespace NativeSpy.IntegrationTests;

public sealed class ObjectSpyCoordinatorTests
{
    private const int ProcessId = 4321;
    private static readonly Point ScreenPoint = new(10, 20);

    [Fact]
    public async Task A_failed_new_freeze_preserves_the_last_healthy_graph_and_quarantines_the_session()
    {
        var root = CreateObject("root", "Root");
        var inspection = new FakeInspectionPort();
        inspection.SetObject(root, Array.Empty<MemberDescriptorDto>(), Array.Empty<MemberReadResultDto>());

        var ui = new FakeUiSession(ProcessId);
        ui.FreezePlan = (_, _) =>
        {
            if (ui.FreezeCount++ == 0)
            {
                return Task.FromResult(CreateFrozenSelection("healthy", available: true));
            }

            ui.IsPoisoned = true;
            throw new FlaUiSessionException("UIA operation timed out.");
        };
        await using var coordinator = CreateCoordinator(ui, inspection, CreateExactTargetPort(root));

        await coordinator.FreezeAsync(ScreenPoint);
        var committedObservation = coordinator.State.SelectionObservation;
        var committedObject = coordinator.State.ManagedObject;
        var committedDescription = coordinator.State.Description;
        Assert.Equal(ObjectSpySelectionState.Frozen, coordinator.State.SelectionState);
        Assert.Equal(ObjectSpyClrState.Ready, coordinator.State.ClrState);

        await coordinator.FreezeAsync(ScreenPoint);

        Assert.Equal(ObjectSpySelectionState.Quarantined, coordinator.State.SelectionState);
        Assert.Equal(ObjectSpyClrState.Ready, coordinator.State.ClrState);
        Assert.Same(committedObservation, coordinator.State.SelectionObservation);
        Assert.Same(committedObject, coordinator.State.ManagedObject);
        Assert.Same(committedDescription, coordinator.State.Description);
        Assert.Contains("timed out", coordinator.State.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_late_freeze_candidate_cannot_overwrite_a_newer_commit()
    {
        var root = CreateObject("root", "Root");
        var inspection = new FakeInspectionPort();
        inspection.SetObject(root, Array.Empty<MemberDescriptorDto>(), Array.Empty<MemberReadResultDto>());
        var firstFreezeStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstFreezeRelease = new TaskCompletionSource<ObjectSpyFrozenSelection>(TaskCreationOptions.RunContinuationsAsynchronously);
        var ui = new FakeUiSession(ProcessId);
        var freezeNumber = 0;
        ui.FreezePlan = async (_, cancellationToken) =>
        {
            if (Interlocked.Increment(ref freezeNumber) == 1)
            {
                firstFreezeStarted.TrySetResult(null);
                return await firstFreezeRelease.Task.WaitAsync(cancellationToken);
            }

            return CreateFrozenSelection("newer", available: true);
        };
        await using var coordinator = CreateCoordinator(ui, inspection, CreateExactTargetPort(root));

        var firstFreeze = coordinator.FreezeAsync(ScreenPoint);
        await firstFreezeStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await coordinator.FreezeAsync(ScreenPoint);
        Assert.Equal("newer", coordinator.State.SelectionObservation!.Name);

        firstFreezeRelease.SetResult(CreateFrozenSelection("older", available: true));
        await firstFreeze;

        Assert.Equal("newer", coordinator.State.SelectionObservation!.Name);
        Assert.Equal(ObjectSpyClrState.Ready, coordinator.State.ClrState);
    }

    [Fact]
    public async Task Preview_state_never_replaces_committed_selection_facts()
    {
        var root = CreateObject("root", "Root");
        var inspection = new FakeInspectionPort();
        inspection.SetObject(root, Array.Empty<MemberDescriptorDto>(), Array.Empty<MemberReadResultDto>());
        var ui = new FakeUiSession(ProcessId);
        ui.FreezePlan = (_, _) => Task.FromResult(CreateFrozenSelection("committed", available: true));
        var preview = new TaskCompletionSource<FlaUiSelectionObservation>(TaskCreationOptions.RunContinuationsAsynchronously);
        var previewStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        ui.PreviewPlan = async (_, cancellationToken) =>
        {
            previewStarted.TrySetResult(null);
            return await preview.Task.WaitAsync(cancellationToken);
        };
        await using var coordinator = CreateCoordinator(ui, inspection, CreateExactTargetPort(root));

        await coordinator.FreezeAsync(ScreenPoint);
        var committedObservation = coordinator.State.SelectionObservation;
        var committedObject = coordinator.State.ManagedObject;
        var previewTask = coordinator.PreviewAsync(ScreenPoint);
        await previewStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(ObjectSpySelectionState.Previewing, coordinator.State.SelectionState);
        Assert.Same(committedObservation, coordinator.State.SelectionObservation);
        Assert.Same(committedObject, coordinator.State.ManagedObject);

        var previewObservation = CreateObservation("preview", available: true);
        preview.SetResult(previewObservation);
        await previewTask;

        Assert.Equal(ObjectSpySelectionState.PreviewReady, coordinator.State.SelectionState);
        Assert.Same(committedObservation, coordinator.State.SelectionObservation);
        Assert.Same(previewObservation, coordinator.State.PreviewObservation);
        Assert.Same(committedObject, coordinator.State.ManagedObject);

        coordinator.AbandonPreview();
        Assert.Equal(ObjectSpySelectionState.Frozen, coordinator.State.SelectionState);
        Assert.Null(coordinator.State.PreviewObservation);
        Assert.Same(committedObservation, coordinator.State.SelectionObservation);
    }

    [Fact]
    public async Task Back_invalidates_a_pending_follow_without_committing_the_late_child()
    {
        var root = CreateObject("root", "Root");
        var child = CreateObject("child", "Child");
        var childMember = CreateField(root, "Child", "child-type");
        var inspection = new FakeInspectionPort();
        inspection.SetObject(
            root,
            new[] { childMember },
            new[]
            {
                new MemberReadResultDto(
                    childMember.Member,
                    ClrReadOutcome.Available,
                    ClrValueDto.CreateObjectReference(child, child.TypeIdentity!))
            });
        inspection.SetObject(child, Array.Empty<MemberDescriptorDto>(), Array.Empty<MemberReadResultDto>());
        var describeGate = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        inspection.GateDescribeFor(child, describeGate);
        var ui = new FakeUiSession(ProcessId)
        {
            FreezePlan = (_, _) => Task.FromResult(CreateFrozenSelection("committed", available: true))
        };
        await using var coordinator = CreateCoordinator(ui, inspection, CreateExactTargetPort(root));
        await coordinator.FreezeAsync(ScreenPoint);

        var childValue = coordinator.State.FieldResults.Single().Value!;
        var followTask = coordinator.FollowObjectReferenceAsync(childValue);
        await inspection.DescribeStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        coordinator.Back();
        Assert.Equal(ObjectSpyClrState.Ready, coordinator.State.ClrState);
        Assert.Same(root, coordinator.State.ManagedObject);
        Assert.Equal(0, coordinator.State.NavigationDepth);

        describeGate.SetResult(null);
        await followTask;

        Assert.Same(root, coordinator.State.ManagedObject);
        Assert.Equal(0, coordinator.State.NavigationDepth);
        Assert.Equal(ObjectSpyClrState.Ready, coordinator.State.ClrState);
    }

    [Fact]
    public async Task Back_invalidates_pending_property_and_member_page_results()
    {
        var root = CreateObject("root", "Root");
        var property = CreateProperty(root, "Name", "string-type");
        var firstField = CreateField(root, "First", "int-type");
        var secondField = CreateField(root, "Second", "int-type");
        var inspection = new FakeInspectionPort();
        inspection.SetObject(
            root,
            new[] { firstField, property },
            new[] { AvailableField(firstField, ClrValueDto.Integer(ClrIntegerKind.Int32, "1")) },
            continuationToken: "page-2");
        inspection.NextPage = new ListMembersResponseDto(new[] { secondField });
        inspection.SetProperty(property, ClrValueDto.String("late", 4, 4, truncated: false));
        var propertyGate = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        inspection.GateProperty(propertyGate);
        var pageGate = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        inspection.GateContinuation(pageGate);
        var ui = new FakeUiSession(ProcessId)
        {
            FreezePlan = (_, _) => Task.FromResult(CreateFrozenSelection("committed", available: true))
        };
        await using var coordinator = CreateCoordinator(ui, inspection, CreateExactTargetPort(root));
        await coordinator.FreezeAsync(ScreenPoint);

        var propertyTask = coordinator.ReadPropertyAsync(property);
        await inspection.PropertyStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        coordinator.Back();
        propertyGate.SetResult(null);
        await propertyTask;
        Assert.Null(coordinator.State.LastReadResult);

        var pageTask = coordinator.LoadNextMemberPageAsync();
        await inspection.ContinuationStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        coordinator.Back();
        pageGate.SetResult(null);
        await pageTask;

        Assert.DoesNotContain(coordinator.State.Members, member => member.Name == "Second");
        Assert.Equal("page-2", coordinator.State.NextContinuationToken);
    }

    [Fact]
    public async Task Non_exact_correlation_stays_frozen_and_does_not_start_clr_inspection()
    {
        var root = CreateObject("root", "Root");
        var inspection = new FakeInspectionPort();
        var ui = new FakeUiSession(ProcessId)
        {
            FreezePlan = (_, _) => Task.FromResult(CreateFrozenSelection("non-exact", available: true))
        };
        var targetPort = CreateNonExactTargetPort();
        await using var coordinator = CreateCoordinator(ui, inspection, targetPort);

        await coordinator.FreezeAsync(ScreenPoint);

        Assert.Equal(ObjectSpySelectionState.Frozen, coordinator.State.SelectionState);
        Assert.Equal(ObjectSpyClrState.Unavailable, coordinator.State.ClrState);
        Assert.Equal(0, inspection.DescribeCalls);
        Assert.Null(coordinator.State.ManagedObject);
    }

    [Fact]
    public async Task Unavailable_external_source_is_stale_and_does_not_start_clr_inspection()
    {
        var root = CreateObject("root", "Root");
        var inspection = new FakeInspectionPort();
        var ui = new FakeUiSession(ProcessId)
        {
            FreezePlan = (_, _) => Task.FromResult(CreateFrozenSelection("stale", available: false))
        };
        await using var coordinator = CreateCoordinator(ui, inspection, CreateExactTargetPort(root));

        await coordinator.FreezeAsync(ScreenPoint);

        Assert.Equal(ObjectSpySelectionState.Stale, coordinator.State.SelectionState);
        Assert.Equal(ObjectSpyClrState.Unavailable, coordinator.State.ClrState);
        Assert.Equal(0, inspection.DescribeCalls);
    }

    [Fact]
    public async Task Stale_handle_inspection_failure_maps_to_stale_without_reclassifying_correlation()
    {
        var root = CreateObject("root", "Root");
        var inspection = new FakeInspectionPort
        {
            DescribeOverride = (_, _) => Task.FromResult(
                ClrInspectionClientResult<DescribeObjectResponseDto>.Failure(
                    new OperationErrorDto(OperationErrorCode.StaleHandle, "stale")))
        };
        var ui = new FakeUiSession(ProcessId)
        {
            FreezePlan = (_, _) => Task.FromResult(CreateFrozenSelection("stale-handle", available: true))
        };
        await using var coordinator = CreateCoordinator(ui, inspection, CreateExactTargetPort(root));

        await coordinator.FreezeAsync(ScreenPoint);

        Assert.Equal(ObjectSpySelectionState.Stale, coordinator.State.SelectionState);
        Assert.Equal(ObjectSpyClrState.Error, coordinator.State.ClrState);
        Assert.Equal(CorrelationStatus.Exact, coordinator.State.Correlation!.Status);
    }

    [Fact]
    public async Task Object_collected_inspection_failure_maps_to_collected_without_losing_frozen_state()
    {
        var root = CreateObject("root", "Root");
        var inspection = new FakeInspectionPort
        {
            DescribeOverride = (_, _) => Task.FromResult(
                ClrInspectionClientResult<DescribeObjectResponseDto>.Failure(
                    new OperationErrorDto(OperationErrorCode.ObjectCollected, "collected")))
        };
        var ui = new FakeUiSession(ProcessId)
        {
            FreezePlan = (_, _) => Task.FromResult(CreateFrozenSelection("collected", available: true))
        };
        await using var coordinator = CreateCoordinator(ui, inspection, CreateExactTargetPort(root));

        await coordinator.FreezeAsync(ScreenPoint);

        Assert.Equal(ObjectSpySelectionState.Frozen, coordinator.State.SelectionState);
        Assert.Equal(ObjectSpyClrState.Collected, coordinator.State.ClrState);
    }

    private static ObjectSpyCoordinator CreateCoordinator(
        FakeUiSession ui,
        FakeInspectionPort inspection,
        IWinFormsCorrelationPort targetPort)
    {
        return new ObjectSpyCoordinator(
            new ProcessIdentityDto(ProcessId, "start"),
            ui,
            targetPort,
            inspection,
            new RecordingOverlay());
    }

    private static ObjectSpyFrozenSelection CreateFrozenSelection(string name, bool available)
    {
        var source = new FakeExternalSource(CreateExternalEvidence(name, available));
        if (available)
        {
            source.EqualityEvidence.Enqueue(CreateEqualityEvidence(source.Evidence.Source, source.Evidence.ObservedHwnd!.Value));
            source.EqualityEvidence.Enqueue(CreateEqualityEvidence(source.Evidence.Source, source.Evidence.ObservedHwnd.Value));
        }

        return new ObjectSpyFrozenSelection(source, CreateObservation(name, available), new TrackingLifetime());
    }

    private static FlaUiSelectionObservation CreateObservation(string name, bool available)
    {
        return new FlaUiSelectionObservation(
            ScreenPoint,
            ProcessId,
            0x1234,
            0x1234,
            new Rectangle(1, 2, 30, 40),
            name,
            "automation-" + name,
            "Window",
            "Button",
            new[] { 1, 2 },
            available,
            available ? null : "The retained source is unavailable.");
    }

    private static ExternalUiaEvidenceDto CreateExternalEvidence(string name, bool available)
    {
        return new ExternalUiaEvidenceDto(
            new ExternalUiaCaptureRefDto("observation-" + name, "capture-" + name),
            ProcessId,
            available ? 0x1234UL : null,
            new[]
            {
                Fact("SourceAvailable", available ? ProofOutcome.Passed : ProofOutcome.Failed),
                Fact("CurrentHwndObserved", available ? ProofOutcome.Passed : ProofOutcome.Failed),
                Fact("ExternalElementProcessIdentity", ProofOutcome.Passed)
            },
            Array.Empty<CorrelationLimitationDto>());
    }

    private static ExternalUiaEqualityEvidenceDto CreateEqualityEvidence(
        ExternalUiaCaptureRefDto source,
        ulong hwnd)
    {
        return new ExternalUiaEqualityEvidenceDto(
            source,
            hwnd,
            new[]
            {
                Fact("SourceAvailable", ProofOutcome.Passed),
                Fact("ElementFromHandle", ProofOutcome.Passed),
                Fact("CompareElements", ProofOutcome.Passed)
            },
            Array.Empty<CorrelationLimitationDto>());
    }

    private static FakeTargetPort CreateExactTargetPort(ManagedObjectRefDto target)
    {
        return new FakeTargetPort(
            CreateInitialTarget(target),
            CreateRevalidatedTarget());
    }

    private static FakeTargetPort CreateNonExactTargetPort()
    {
        return new FakeTargetPort(
            CreateInitialTarget(candidate: null),
            CreateRevalidatedTarget());
    }

    private static FrameworkCorrelationEvidenceDto CreateInitialTarget(ManagedObjectRefDto? candidate)
    {
        return new FrameworkCorrelationEvidenceDto(
            "winforms",
            ProcessId,
            candidate is null
                ? null
                : new CorrelationTargetRefDto(
                    CorrelationTargetKind.ManagedObject,
                    managed: candidate),
            new[] { Fact("ControlFromHandle", candidate is null ? ProofOutcome.Failed : ProofOutcome.Passed) },
            new[]
            {
                Validation("CurrentHwndMatches", candidate is null ? ValidationOutcome.Failed : ValidationOutcome.Passed),
                Validation("ControlLive", candidate is null ? ValidationOutcome.Failed : ValidationOutcome.Passed)
            },
            PassiveEffects(),
            Array.Empty<AdapterMetadataDto>(),
            Array.Empty<CorrelationLimitationDto>());
    }

    private static FrameworkCorrelationEvidenceDto CreateRevalidatedTarget()
    {
        return new FrameworkCorrelationEvidenceDto(
            "winforms",
            ProcessId,
            candidateTarget: null,
            new[]
            {
                Fact("CandidateResolved", ProofOutcome.Passed),
                Fact("ControlFromHandleReferenceEqual", ProofOutcome.Passed),
                Fact("ControlFromHandle", ProofOutcome.Passed)
            },
            new[]
            {
                Validation("CandidateResolved", ValidationOutcome.Passed),
                Validation("CurrentHwndMatches", ValidationOutcome.Passed),
                Validation("ControlLive", ValidationOutcome.Passed)
            },
            PassiveEffects(),
            Array.Empty<AdapterMetadataDto>(),
            Array.Empty<CorrelationLimitationDto>());
    }

    private static ManagedObjectRefDto CreateObject(string handleId, string name)
    {
        var type = new TypeIdentityDto(
            name + "-type",
            "Example." + name,
            "Example",
            "boundary",
            isValueType: false,
            Array.Empty<TypeRefDto>(),
            Array.Empty<TypeRefDto>());
        return new ManagedObjectRefDto(
            new HandleRefDto("session", handleId, 1, HandleKind.ClrObject, "boundary"),
            type,
            "boundary");
    }

    private static MemberDescriptorDto CreateField(
        ManagedObjectRefDto declaringObject,
        string name,
        string valueTypeId)
    {
        var member = new MemberRefDto(
            "session",
            declaringObject.Handle.HandleId + "-" + name,
            "boundary",
            declaringObject.TypeIdentity!.TypeId);
        return new MemberDescriptorDto(
            member,
            ClrMemberKind.Field,
            name,
            new TypeRefDto(valueTypeId, "boundary"));
    }

    private static MemberDescriptorDto CreateProperty(
        ManagedObjectRefDto declaringObject,
        string name,
        string valueTypeId)
    {
        var member = new MemberRefDto(
            "session",
            declaringObject.Handle.HandleId + "-" + name,
            "boundary",
            declaringObject.TypeIdentity!.TypeId);
        return new MemberDescriptorDto(
            member,
            ClrMemberKind.Property,
            name,
            new TypeRefDto(valueTypeId, "boundary"));
    }

    private static MemberReadResultDto AvailableField(MemberDescriptorDto member, ClrValueDto value)
    {
        return new MemberReadResultDto(member.Member, ClrReadOutcome.Available, value);
    }

    private static CorrelationEvidenceFactDto Fact(string name, ProofOutcome outcome)
    {
        return new CorrelationEvidenceFactDto(name, outcome, EvidenceKind.Deterministic);
    }

    private static CorrelationValidationFactDto Validation(string name, ValidationOutcome outcome)
    {
        return new CorrelationValidationFactDto(name, outcome);
    }

    private static CorrelationEffectSummaryDto PassiveEffects()
    {
        return new CorrelationEffectSummaryDto(
            new[] { EffectCategory.Passive },
            FrameworkStateEffect.None,
            ApplicationCallbackEffect.None,
            Array.Empty<CallbackDetailDto>(),
            VisibleMutationEffect.NotRequested,
            Array.Empty<string>());
    }

    private sealed class FakeUiSession : IObjectSpyUiSession
    {
        public FakeUiSession(int processId)
        {
            ProcessId = processId;
        }

        public int ProcessId { get; }

        public bool IsPoisoned { get; set; }

        public int FreezeCount { get; set; }

        public Func<Point, CancellationToken, Task<FlaUiSelectionObservation>> PreviewPlan { get; set; } =
            (_, _) => Task.FromResult(CreateObservation("preview", available: true));

        public Func<Point, CancellationToken, Task<ObjectSpyFrozenSelection>> FreezePlan { get; set; } =
            (_, _) => throw new InvalidOperationException("No freeze plan configured.");

        public Task<FlaUiSelectionObservation> PreviewFromPointAsync(
            Point screenPoint,
            CancellationToken cancellationToken)
        {
            return PreviewPlan(screenPoint, cancellationToken);
        }

        public Task<ObjectSpyFrozenSelection> FreezeFromPointAsync(
            Point screenPoint,
            CancellationToken cancellationToken)
        {
            return FreezePlan(screenPoint, cancellationToken);
        }
    }

    private sealed class FakeExternalSource : IExternalUiaObservationPort
    {
        public FakeExternalSource(ExternalUiaEvidenceDto evidence)
        {
            Evidence = evidence;
        }

        public ExternalUiaEvidenceDto Evidence { get; }

        public Queue<ExternalUiaEqualityEvidenceDto> EqualityEvidence { get; } = new();

        public Task<ExternalUiaEvidenceDto> CaptureAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Evidence);
        }

        public Task<ExternalUiaEqualityEvidenceDto> CompareWithHwndAsync(
            ExternalUiaCaptureRefDto capture,
            ulong hwnd,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(EqualityEvidence.Dequeue());
        }
    }

    private sealed class FakeTargetPort : IWinFormsCorrelationPort
    {
        public FakeTargetPort(
            FrameworkCorrelationEvidenceDto initial,
            FrameworkCorrelationEvidenceDto revalidated)
        {
            Initial = initial;
            Revalidated = revalidated;
        }

        public FrameworkCorrelationEvidenceDto Initial { get; }

        public FrameworkCorrelationEvidenceDto Revalidated { get; }

        public Task<FrameworkCorrelationEvidenceDto> BeginCurrentHwndAsync(
            ulong hwnd,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Initial);
        }

        public Task<FrameworkCorrelationEvidenceDto> RevalidateCurrentHwndAsync(
            ulong hwnd,
            HandleRefDto candidateHandle,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Revalidated);
        }
    }

    private sealed class FakeInspectionPort : IClrInspectionPort
    {
        private readonly Dictionary<string, ClrInspectionClientResult<DescribeObjectResponseDto>> _descriptions = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ClrInspectionClientResult<ListMembersResponseDto>> _members = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ClrInspectionClientResult<ReadFieldValuesResponseDto>> _fields = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ClrInspectionClientResult<ReadPropertyValueResponseDto>> _properties = new(StringComparer.Ordinal);

        public int DescribeCalls { get; private set; }

        public Func<ManagedObjectRefDto, CancellationToken, Task<ClrInspectionClientResult<DescribeObjectResponseDto>>>? DescribeOverride { get; set; }

        public ListMembersResponseDto? NextPage { get; set; }

        public TaskCompletionSource<object?> DescribeStarted { get; } = NewSignal();

        public TaskCompletionSource<object?> PropertyStarted { get; } = NewSignal();

        public TaskCompletionSource<object?> ContinuationStarted { get; } = NewSignal();

        private TaskCompletionSource<object?>? _describeGate;
        private string? _describeGateHandleId;
        private TaskCompletionSource<object?>? _propertyGate;
        private TaskCompletionSource<object?>? _continuationGate;

        public void SetObject(
            ManagedObjectRefDto @object,
            IReadOnlyList<MemberDescriptorDto> members,
            IReadOnlyList<MemberReadResultDto> fields,
            string? continuationToken = null)
        {
            var key = @object.Handle.HandleId;
            _descriptions[key] = ClrInspectionClientResult<DescribeObjectResponseDto>.Success(
                new DescribeObjectResponseDto(@object));
            _members[key] = ClrInspectionClientResult<ListMembersResponseDto>.Success(
                new ListMembersResponseDto(members, continuationToken));
            _fields[key] = ClrInspectionClientResult<ReadFieldValuesResponseDto>.Success(
                new ReadFieldValuesResponseDto(fields));
        }

        public void SetProperty(MemberDescriptorDto member, ClrValueDto value)
        {
            _properties[member.Member.MemberId] = ClrInspectionClientResult<ReadPropertyValueResponseDto>.Success(
                new ReadPropertyValueResponseDto(
                    new MemberReadResultDto(member.Member, ClrReadOutcome.Available, value)));
        }

        public void GateDescribeFor(ManagedObjectRefDto @object, TaskCompletionSource<object?> gate)
        {
            _describeGateHandleId = @object.Handle.HandleId;
            _describeGate = gate;
        }

        public void GateProperty(TaskCompletionSource<object?> gate)
        {
            _propertyGate = gate;
        }

        public void GateContinuation(TaskCompletionSource<object?> gate)
        {
            _continuationGate = gate;
        }

        public async Task<ClrInspectionClientResult<DescribeObjectResponseDto>> DescribeObjectAsync(
            ManagedObjectRefDto @object,
            CancellationToken cancellationToken)
        {
            DescribeCalls++;
            if (_describeGate is not null && string.Equals(_describeGateHandleId, @object.Handle.HandleId, StringComparison.Ordinal))
            {
                DescribeStarted.TrySetResult(null);
                await _describeGate.Task.WaitAsync(cancellationToken);
            }

            if (DescribeOverride is not null)
            {
                return await DescribeOverride(@object, cancellationToken);
            }

            return _descriptions[@object.Handle.HandleId];
        }

        public async Task<ClrInspectionClientResult<ListMembersResponseDto>> ListMembersAsync(
            ManagedObjectRefDto @object,
            int pageSize,
            ClrMemberKindFilter filter,
            string? continuationToken,
            CancellationToken cancellationToken)
        {
            if (continuationToken is not null && _continuationGate is not null)
            {
                ContinuationStarted.TrySetResult(null);
                await _continuationGate.Task.WaitAsync(cancellationToken);
                return ClrInspectionClientResult<ListMembersResponseDto>.Success(NextPage!);
            }

            return _members[@object.Handle.HandleId];
        }

        public Task<ClrInspectionClientResult<ReadFieldValuesResponseDto>> ReadFieldValuesAsync(
            ManagedObjectRefDto @object,
            IReadOnlyList<MemberRefDto> members,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_fields[@object.Handle.HandleId]);
        }

        public async Task<ClrInspectionClientResult<ReadPropertyValueResponseDto>> ReadPropertyValueAsync(
            ManagedObjectRefDto @object,
            MemberRefDto member,
            CancellationToken cancellationToken)
        {
            if (_propertyGate is not null)
            {
                PropertyStarted.TrySetResult(null);
                await _propertyGate.Task.WaitAsync(cancellationToken);
            }

            return _properties[member.MemberId];
        }

        private static TaskCompletionSource<object?> NewSignal()
        {
            return new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    private sealed class RecordingOverlay : IObjectSpyOverlay
    {
        public List<long> Clears { get; } = new();

        public void ShowPreview(ObjectSpyOverlayGeometry geometry, long generation)
        {
        }

        public void Clear(long generation)
        {
            Clears.Add(generation);
        }
    }

    private sealed class TrackingLifetime : IAsyncDisposable
    {
        public int DisposeCount { get; private set; }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }
}
