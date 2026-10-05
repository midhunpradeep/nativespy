using System.Drawing;
using System.Runtime.InteropServices;
using NativeSpy.FlaUI;
using NativeSpy.ObjectSpy;
using NativeSpy.Protocol.Clr;
using NativeSpy.Protocol.Correlation;
using Xunit;
using Xunit.Sdk;

namespace NativeSpy.IntegrationTests;

[Collection("NativeSpy live UI")]
public sealed class ObjectSpyEndToEndTests
{
    [Fact]
    public async Task ObjectSpy_freeze_inspects_getters_and_navigates_references()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw SkipException.ForSkip("The ObjectSpy end-to-end proof requires Windows UI Automation.");
        }

        using var target = TestTargetProcess.Start();
        using var flaUi = FlaUiAutomationSession.Attach(target.ProcessIdentity);
        var source = flaUi.FindByAutomationId("NativeSpyTestButton");
        var capture = await source.CaptureAsync(CancellationToken.None);
        Assert.NotNull(capture.ObservedHwnd);
        var rootHwnd = GetAncestor(
            new IntPtr(unchecked((long)capture.ObservedHwnd!.Value)),
            2);
        Assert.NotEqual(IntPtr.Zero, rootHwnd);
        _ = SetWindowPos(rootHwnd, new IntPtr(-1), 0, 0, 0, 0, 0x0043);
        _ = SetForegroundWindow(rootHwnd);
        var targetPoint = new NativePoint { X = 10, Y = 10 };
        Assert.True(ClientToScreen(rootHwnd, ref targetPoint));

        await using var coordinator = new ObjectSpyCoordinator(
            target.ProcessIdentity,
            flaUi,
            target.Session,
            target.Session);
        await coordinator.FreezeAsync(new Point(targetPoint.X, targetPoint.Y));

        Assert.Equal(ObjectSpySelectionState.Frozen, coordinator.State.SelectionState);
        Assert.Equal(ObjectSpyClrState.Ready, coordinator.State.ClrState);
        Assert.Equal(CorrelationStatus.Exact, coordinator.State.Correlation!.Status);
        Assert.Equal(
            "NativeSpy.TestTarget.WinForms.MainForm",
            coordinator.State.Description!.Object.TypeIdentity!.FullName);
        Assert.Contains(
            coordinator.State.Members,
            member => member.Name == "PublicText" && member.Kind == ClrMemberKind.Field);
        Assert.Contains(
            coordinator.State.Members,
            member => member.Name == "CountingProperty" && member.Kind == ClrMemberKind.Property);

        var counting = Assert.Single(
            coordinator.State.Members,
            member => member.Name == "CountingProperty");
        await coordinator.ReadPropertyAsync(counting);
        Assert.Equal(ClrReadOutcome.Available, coordinator.State.LastReadResult!.Outcome);
        Assert.Equal("getter value", coordinator.State.LastReadResult.Value!.StringValue);

        var childField = Assert.Single(
            coordinator.State.FieldResults,
            result => result.Value?.Kind == ClrValueKind.ObjectReference);
        await coordinator.FollowObjectReferenceAsync(childField.Value!);
        Assert.Equal(ObjectSpyClrState.Ready, coordinator.State.ClrState);
        Assert.Equal(
            "NativeSpy.TestTarget.WinForms.InspectableChild",
            coordinator.State.Description!.Object.TypeIdentity!.FullName);
        Assert.Equal(1, coordinator.State.NavigationDepth);
        coordinator.Back();
        Assert.Equal(0, coordinator.State.NavigationDepth);
        Assert.Equal(
            "NativeSpy.TestTarget.WinForms.MainForm",
            coordinator.State.Description!.Object.TypeIdentity!.FullName);
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
    private static extern bool ClientToScreen(IntPtr hwnd, ref NativePoint point);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

    private struct NativePoint
    {
        public int X;
        public int Y;
    }
}
