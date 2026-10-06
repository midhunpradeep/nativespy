using System.Drawing;
using System.Runtime.InteropServices;
using NativeSpy.Client.ClrInspection;
using NativeSpy.FlaUI;
using NativeSpy.ObjectSpy;
using NativeSpy.Protocol.Clr;
using NativeSpy.Protocol.Common;
using NativeSpy.Protocol.Correlation;
using Xunit;
using Xunit.Sdk;

namespace NativeSpy.IntegrationTests;

[Collection("NativeSpy live UI")]
public sealed class WpfChildExclusionTests
{
    [Fact]
    public async Task Hwndless_wpf_child_resolves_host_root_and_preview_and_freeze_are_excluded()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw SkipException.ForSkip("The WPF UI Automation proof requires Windows.");
        }

        using var target = WpfTestTargetProcess.Start();
        using var flaUi = FlaUiAutomationSession.Attach(target.ProcessIdentity);
        var childSource = flaUi.FindByAutomationId("NativeSpyWpfChildButton");
        var capture = await childSource.CaptureAsync(CancellationToken.None);
        Assert.Null(capture.ObservedHwnd);

        BringToFront(target.MainWindowHandle);
        var childPoint = GetClientCenter(target.MainWindowHandle);
        var observation = await flaUi.PreviewFromPointAsync(childPoint);
        var topLevelHwnd = ToHwndValue(target.MainWindowHandle);
        Assert.Null(observation.CandidateHwnd);
        Assert.Equal(topLevelHwnd, observation.RootHwnd);
        Assert.Equal(target.ProcessId, observation.CandidateProcessId);
        Assert.True(observation.IsAvailable);

        flaUi.RegisterExcludedWindow(topLevelHwnd);
        try
        {
            await Assert.ThrowsAsync<FlaUiExcludedWindowException>(() =>
                flaUi.PreviewFromPointAsync(childPoint));
            await Assert.ThrowsAsync<FlaUiExcludedWindowException>(() =>
                flaUi.FreezeFromPointAsync(childPoint));
        }
        finally
        {
            flaUi.UnregisterExcludedWindow(topLevelHwnd);
        }
    }

    [Fact]
    public async Task Excluded_hwndless_wpf_child_preserves_committed_winforms_selection_and_clr()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw SkipException.ForSkip("The WPF coordinator proof requires Windows.");
        }

        using var target = TestTargetProcess.Start();
        using var flaUi = FlaUiAutomationSession.Attach(target.ProcessIdentity);
        var targetPoint = GetTargetButtonPoint(flaUi);
        var inspection = new CountingInspectionPort(target.Session);
        await using var coordinator = new ObjectSpyCoordinator(
            target.ProcessIdentity,
            flaUi,
            target.Session,
            inspection);

        await coordinator.FreezeAsync(targetPoint);
        Assert.Equal(ObjectSpySelectionState.Frozen, coordinator.State.SelectionState);
        Assert.Equal(ObjectSpyClrState.Ready, coordinator.State.ClrState);
        Assert.Equal(CorrelationStatus.Exact, coordinator.State.Correlation!.Status);
        var committedObservation = coordinator.State.SelectionObservation;
        var committedObject = coordinator.State.ManagedObject;
        var committedDescription = coordinator.State.Description;
        var committedGeneration = coordinator.State.Generation;
        var baselineInspectionCalls = inspection.Calls;

        using var wpfTarget = WpfTestTargetProcess.Start();
        BringToFront(wpfTarget.MainWindowHandle);
        var childPoint = GetClientCenter(wpfTarget.MainWindowHandle);
        var rootHwnd = ToHwndValue(wpfTarget.MainWindowHandle);
        var rawObservation = await flaUi.PreviewFromPointAsync(childPoint);
        Assert.Null(rawObservation.CandidateHwnd);
        Assert.Equal(rootHwnd, rawObservation.RootHwnd);
        Assert.Equal(wpfTarget.ProcessId, rawObservation.CandidateProcessId);

        flaUi.RegisterExcludedWindow(rootHwnd);
        try
        {
            await coordinator.PreviewAsync(childPoint);
            Assert.Equal(ObjectSpySelectionState.Frozen, coordinator.State.SelectionState);
            Assert.Equal(ObjectSpyClrState.Ready, coordinator.State.ClrState);
            Assert.Null(coordinator.State.PreviewObservation);
            Assert.Null(coordinator.State.Error);
            Assert.Same(committedObservation, coordinator.State.SelectionObservation);
            Assert.Same(committedObject, coordinator.State.ManagedObject);
            Assert.Same(committedDescription, coordinator.State.Description);
            Assert.Equal(committedGeneration, coordinator.State.Generation);
            Assert.Equal(baselineInspectionCalls, inspection.Calls);

            await coordinator.FreezeAsync(childPoint);
            Assert.Equal(ObjectSpySelectionState.Frozen, coordinator.State.SelectionState);
            Assert.Equal(ObjectSpyClrState.Ready, coordinator.State.ClrState);
            Assert.Same(committedObservation, coordinator.State.SelectionObservation);
            Assert.Same(committedObject, coordinator.State.ManagedObject);
            Assert.Same(committedDescription, coordinator.State.Description);
            Assert.Equal(committedGeneration, coordinator.State.Generation);
            Assert.Equal(baselineInspectionCalls, inspection.Calls);
        }
        finally
        {
            flaUi.UnregisterExcludedWindow(rootHwnd);
        }
    }

    private static Point GetTargetButtonPoint(FlaUiAutomationSession flaUi)
    {
        var source = flaUi.FindByAutomationId("NativeSpyTestButton");
        var capture = source.CaptureAsync(CancellationToken.None).GetAwaiter().GetResult();
        Assert.NotNull(capture.ObservedHwnd);
        var buttonHwnd = new IntPtr(unchecked((long)capture.ObservedHwnd!.Value));
        var root = GetAncestor(buttonHwnd, 2);
        Assert.NotEqual(IntPtr.Zero, root);
        BringToFront(root);
        Assert.True(GetWindowRect(buttonHwnd, out var rectangle));
        return new Point(
            rectangle.Left + Math.Max(1, (rectangle.Right - rectangle.Left) / 2),
            rectangle.Top + Math.Max(1, (rectangle.Bottom - rectangle.Top) / 2));
    }

    private static void BringToFront(IntPtr hwnd)
    {
        Assert.NotEqual(IntPtr.Zero, hwnd);
        Assert.True(SetWindowPos(
            hwnd,
            new IntPtr(-1),
            0,
            0,
            520,
            320,
            0x0040));
        _ = SetForegroundWindow(hwnd);
    }

    private static Point GetClientCenter(IntPtr hwnd)
    {
        Assert.True(GetClientRect(hwnd, out var rectangle));
        var point = new NativePoint
        {
            X = rectangle.Left + (rectangle.Right - rectangle.Left) / 2,
            Y = rectangle.Top + (rectangle.Bottom - rectangle.Top) / 2
        };
        Assert.True(ClientToScreen(hwnd, ref point));
        return new Point(point.X, point.Y);
    }

    private static ulong ToHwndValue(IntPtr hwnd)
    {
        Assert.NotEqual(IntPtr.Zero, hwnd);
        return unchecked((ulong)hwnd.ToInt64());
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hwnd,
        IntPtr hwndInsertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetClientRect(IntPtr hwnd, out NativeRect rectangle);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ClientToScreen(IntPtr hwnd, ref NativePoint point);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rectangle);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

    private sealed class CountingInspectionPort : IClrInspectionPort
    {
        private readonly IClrInspectionPort _inner;

        public CountingInspectionPort(IClrInspectionPort inner)
        {
            _inner = inner;
        }

        public InspectionCallCounts Calls => new(
            Volatile.Read(ref _describeCalls),
            Volatile.Read(ref _listMembersCalls),
            Volatile.Read(ref _readFieldValuesCalls),
            Volatile.Read(ref _readPropertyCalls));

        private int _describeCalls;
        private int _listMembersCalls;
        private int _readFieldValuesCalls;
        private int _readPropertyCalls;

        public Task<ClrInspectionClientResult<DescribeObjectResponseDto>> DescribeObjectAsync(
            ManagedObjectRefDto @object,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _describeCalls);
            return _inner.DescribeObjectAsync(@object, cancellationToken);
        }

        public Task<ClrInspectionClientResult<ListMembersResponseDto>> ListMembersAsync(
            ManagedObjectRefDto @object,
            int pageSize,
            ClrMemberKindFilter filter,
            string? continuationToken,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _listMembersCalls);
            return _inner.ListMembersAsync(@object, pageSize, filter, continuationToken, cancellationToken);
        }

        public Task<ClrInspectionClientResult<ReadFieldValuesResponseDto>> ReadFieldValuesAsync(
            ManagedObjectRefDto @object,
            IReadOnlyList<MemberRefDto> members,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _readFieldValuesCalls);
            return _inner.ReadFieldValuesAsync(@object, members, cancellationToken);
        }

        public Task<ClrInspectionClientResult<ReadPropertyValueResponseDto>> ReadPropertyValueAsync(
            ManagedObjectRefDto @object,
            MemberRefDto member,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _readPropertyCalls);
            return _inner.ReadPropertyValueAsync(@object, member, cancellationToken);
        }
    }

    private readonly record struct InspectionCallCounts(
        int DescribeCalls,
        int ListMembersCalls,
        int ReadFieldValuesCalls,
        int ReadPropertyCalls);

    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private struct NativePoint
    {
        public int X;
        public int Y;
    }
}
