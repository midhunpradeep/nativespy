using System.Drawing;
using System.Runtime.InteropServices;
using NativeSpy.Client.NamedPipes;
using NativeSpy.FlaUI;
using NativeSpy.ObjectSpy;
using NativeSpy.Protocol.Clr;
using NativeSpy.Protocol.Common;
using NativeSpy.Protocol.Correlation;
using NativeSpy.Protocol.Json;
using Xunit;

namespace NativeSpy.IntegrationTests;

[Collection("NativeSpy live UI")]
public sealed class SlowGetterIntegrationTests
{
    [Fact]
    public async Task Getter_deadlines_distinguish_queued_and_started_work_without_duplicate_responses()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var suffix = Guid.NewGuid().ToString("N");
        var blockingStartedName = $"NativeSpy-BlockingGetter-Started-{suffix}";
        var blockingReleaseName = $"NativeSpy-BlockingGetter-Release-{suffix}";
        var blockingFinishedName = $"NativeSpy-BlockingGetter-Finished-{suffix}";
        var countingStartedName = $"NativeSpy-CountingGetter-Started-{suffix}";
        using var blockingStarted = CreateEvent(blockingStartedName);
        using var blockingRelease = CreateEvent(blockingReleaseName);
        using var blockingFinished = CreateEvent(blockingFinishedName);
        using var countingStarted = CreateEvent(countingStartedName);
        var environment = new Dictionary<string, string>
        {
            ["NATIVESPY_BLOCKING_GETTER_STARTED_EVENT"] = blockingStartedName,
            ["NATIVESPY_BLOCKING_GETTER_RELEASE_EVENT"] = blockingReleaseName,
            ["NATIVESPY_BLOCKING_GETTER_FINISHED_EVENT"] = blockingFinishedName,
            ["NATIVESPY_COUNTING_GETTER_EVENT"] = countingStartedName
        };

        using var target = TestTargetProcess.Start(environment);
        using var flaUi = FlaUiAutomationSession.Attach(target.ProcessIdentity);
        var source = flaUi.FindByAutomationId("NativeSpyTestButton");
        var capture = await source.CaptureAsync(CancellationToken.None);
        Assert.NotNull(capture.ObservedHwnd);
        var root = GetAncestor(
            new IntPtr(unchecked((long)capture.ObservedHwnd!.Value)),
            2);
        Assert.NotEqual(IntPtr.Zero, root);
        _ = SetWindowPos(root, new IntPtr(-1), 0, 0, 460, 220, 0x0010 | 0x0040);
        _ = SetForegroundWindow(root);
        var nativePoint = new NativePoint { X = 10, Y = 10 };
        Assert.True(ClientToScreen(root, ref nativePoint));
        var point = new Point(nativePoint.X, nativePoint.Y);

        await using var coordinator = new ObjectSpyCoordinator(
            target.ProcessIdentity,
            flaUi,
            target.Session,
            target.Session);
        try
        {
            await coordinator.FreezeAsync(point);
            Assert.Equal(ObjectSpyClrState.Ready, coordinator.State.ClrState);
            var propertyMembers = await LoadAllPropertiesAsync(
                target.Session,
                coordinator.State.ManagedObject!);
            var blockingMember = Assert.Single(
                propertyMembers,
                member => member.Name == "BlockingProperty");
            var countingMember = Assert.Single(
                propertyMembers,
                member => member.Name == "CountingProperty");
            var blockingRequest = CreatePropertyRequest(
                coordinator.State.ManagedObject!,
                blockingMember);
            var countingRequest = CreatePropertyRequest(
                coordinator.State.ManagedObject!,
                countingMember);

            var startedBlocking = target.Session.SendRequestAsync(
                NamedPipeOperationNames.ReadPropertyValue,
                ClrInspectionJsonCodec.CreateReadPropertyValuePayload(blockingRequest),
                budgetMs: 5_000,
                cancellationToken: CancellationToken.None);
            Assert.True(await WaitForEventAsync(blockingStarted));

            var queuedCounting = target.Session.SendRequestAsync(
                NamedPipeOperationNames.ReadPropertyValue,
                ClrInspectionJsonCodec.CreateReadPropertyValuePayload(countingRequest),
                budgetMs: 25,
                cancellationToken: CancellationToken.None);
            var queuedResponse = await queuedCounting.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(ProtocolJsonCodec.OperationErrorStatus, queuedResponse.ResultStatus);
            Assert.Equal(nameof(OperationErrorCode.TargetTimeout), queuedResponse.OperationError?.Code);
            Assert.False(countingStarted.WaitOne(TimeSpan.Zero));

            blockingRelease.Set();
            var startedResponse = await startedBlocking.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(ProtocolJsonCodec.SuccessStatus, startedResponse.ResultStatus);
            Assert.True(await WaitForEventAsync(blockingFinished));
            Assert.False(countingStarted.WaitOne(TimeSpan.FromMilliseconds(250)));

            blockingStarted.Reset();
            blockingFinished.Reset();
            blockingRelease.Reset();
            var lateBlocking = target.Session.SendRequestAsync(
                NamedPipeOperationNames.ReadPropertyValue,
                ClrInspectionJsonCodec.CreateReadPropertyValuePayload(blockingRequest),
                budgetMs: 25,
                cancellationToken: CancellationToken.None);
            Assert.True(await WaitForEventAsync(blockingStarted));
            var timeoutResponse = await lateBlocking.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(ProtocolJsonCodec.OperationErrorStatus, timeoutResponse.ResultStatus);
            Assert.Equal(nameof(OperationErrorCode.TargetTimeout), timeoutResponse.OperationError?.Code);
            Assert.False(blockingFinished.WaitOne(TimeSpan.Zero));
            Assert.False(target.HasExited);

            blockingRelease.Set();
            Assert.True(await WaitForEventAsync(blockingFinished));
        }
        finally
        {
            blockingRelease.Set();
        }
    }

    private static async Task<IReadOnlyList<MemberDescriptorDto>> LoadAllPropertiesAsync(
        NativeSpy.Client.NamedPipes.NamedPipeClientSession session,
        ManagedObjectRefDto @object)
    {
        var members = new List<MemberDescriptorDto>();
        string? continuation = null;
        do
        {
            var response = await session.ListMembersAsync(
                @object,
                128,
                ClrMemberKindFilter.Properties,
                continuation,
                CancellationToken.None);
            Assert.True(response.IsSuccess, response.Error?.Message);
            members.AddRange(response.Value!.Members);
            continuation = response.Value.NextContinuationToken;
        }
        while (continuation is not null);

        return members;
    }

    private static ReadPropertyValueRequestDto CreatePropertyRequest(
        ManagedObjectRefDto @object,
        MemberDescriptorDto member)
    {
        return new ReadPropertyValueRequestDto(@object, member.Member);
    }

    private static EventWaitHandle CreateEvent(string name)
    {
        return new EventWaitHandle(false, EventResetMode.ManualReset, name);
    }

    private static Task<bool> WaitForEventAsync(EventWaitHandle signal)
    {
        return Task.Run(() => signal.WaitOne(TimeSpan.FromSeconds(5)));
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
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ClientToScreen(IntPtr hwnd, ref NativePoint point);

    private struct NativePoint
    {
        public int X;
        public int Y;
    }
}
