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
        Assert.True(GetWindowRect(rootHwnd, out var nativeRect));

        await using var coordinator = new ObjectSpyCoordinator(
            target.ProcessIdentity,
            flaUi,
            target.Session,
            target.Session);
        await coordinator.FreezeAsync(
            new Point(nativeRect.Left + 24, nativeRect.Top + 80));

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
    private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rectangle);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
