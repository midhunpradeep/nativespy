using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
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
    public async Task Live_point_preview_and_explicit_window_exclusion_preserve_committed_selection()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw SkipException.ForSkip("The live point-preview proof requires Windows UI Automation.");
        }

        using var target = TestTargetProcess.Start();
        using var flaUi = FlaUiAutomationSession.Attach(target.ProcessIdentity);
        var source = flaUi.FindByAutomationId("NativeSpyTestButton");
        var capture = await source.CaptureAsync(CancellationToken.None);
        Assert.NotNull(capture.ObservedHwnd);
        var targetRoot = GetAncestor(
            new IntPtr(unchecked((long)capture.ObservedHwnd!.Value)),
            2);
        Assert.NotEqual(IntPtr.Zero, targetRoot);
        _ = SetWindowPos(targetRoot, new IntPtr(-1), 0, 0, 460, 220, 0x0010 | 0x0040);
        _ = SetForegroundWindow(targetRoot);
        var buttonBounds = GetWindowBounds(new IntPtr(unchecked((long)capture.ObservedHwnd.Value)));
        var targetPoint = new Point(
            buttonBounds.Left + Math.Max(1, buttonBounds.Width / 2),
            buttonBounds.Top + Math.Max(1, buttonBounds.Height / 2));

        var preview = await flaUi.PreviewFromPointAsync(targetPoint);
        Assert.True(preview.IsAvailable);
        Assert.Equal(target.ProcessId, preview.CandidateProcessId);
        Assert.NotNull(preview.Bounds);
        Assert.True(preview.Bounds.Value.Width > 0);
        Assert.True(preview.Bounds.Value.Height > 0);

        await using var coordinator = new ObjectSpyCoordinator(
            target.ProcessIdentity,
            flaUi,
            target.Session,
            target.Session);
        await coordinator.FreezeAsync(targetPoint);
        Assert.Equal(ObjectSpySelectionState.Frozen, coordinator.State.SelectionState);
        Assert.Equal(ObjectSpyClrState.Ready, coordinator.State.ClrState);
        var committedObservation = coordinator.State.SelectionObservation;
        var committedObject = coordinator.State.ManagedObject;
        var committedDescription = coordinator.State.Description;
        var committedGeneration = coordinator.State.Generation;

        using var testOverlay = TestOverlayWindow.Start(new Point(650, 20));
        var overlayBounds = GetWindowBounds(testOverlay.Handle);
        var overlayPoint = new Point(
            overlayBounds.Left + Math.Max(1, overlayBounds.Width / 2),
            overlayBounds.Top + Math.Max(1, overlayBounds.Height / 2));
        var targetRootValue = unchecked((ulong)targetRoot.ToInt64());
        var overlayValue = unchecked((ulong)testOverlay.Handle.ToInt64());
        flaUi.RegisterExcludedWindow(targetRootValue);
        flaUi.RegisterExcludedWindow(overlayValue);
        try
        {
            await Assert.ThrowsAsync<FlaUiExcludedWindowException>(() =>
                flaUi.PreviewFromPointAsync(targetPoint));
            await Assert.ThrowsAsync<FlaUiExcludedWindowException>(() =>
                flaUi.FreezeFromPointAsync(targetPoint));
            await Assert.ThrowsAsync<FlaUiExcludedWindowException>(() =>
                flaUi.PreviewFromPointAsync(overlayPoint));
            await Assert.ThrowsAsync<FlaUiExcludedWindowException>(() =>
                flaUi.FreezeFromPointAsync(overlayPoint));

            await coordinator.PreviewAsync(targetPoint);
            Assert.Equal(ObjectSpySelectionState.Frozen, coordinator.State.SelectionState);
            Assert.Null(coordinator.State.PreviewObservation);
            Assert.Null(coordinator.State.Error);
            Assert.Same(committedObservation, coordinator.State.SelectionObservation);
            Assert.Same(committedObject, coordinator.State.ManagedObject);
            Assert.Same(committedDescription, coordinator.State.Description);
            Assert.Equal(committedGeneration, coordinator.State.Generation);

            await coordinator.FreezeAsync(overlayPoint);
            Assert.Equal(ObjectSpySelectionState.Frozen, coordinator.State.SelectionState);
            Assert.Equal(ObjectSpyClrState.Ready, coordinator.State.ClrState);
            Assert.Same(committedObservation, coordinator.State.SelectionObservation);
            Assert.Same(committedObject, coordinator.State.ManagedObject);
            Assert.Equal(committedGeneration, coordinator.State.Generation);
        }
        finally
        {
            flaUi.UnregisterExcludedWindow(overlayValue);
            flaUi.UnregisterExcludedWindow(targetRootValue);
        }

        var normalAgain = await flaUi.PreviewFromPointAsync(targetPoint);
        Assert.True(normalAgain.IsAvailable);
        Assert.Equal(target.ProcessId, normalAgain.CandidateProcessId);
    }

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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rectangle);

    private static Rectangle GetWindowBounds(IntPtr hwnd)
    {
        Assert.True(GetWindowRect(hwnd, out var rectangle));
        return Rectangle.FromLTRB(rectangle.Left, rectangle.Top, rectangle.Right, rectangle.Bottom);
    }

    private sealed class TestOverlayWindow : IDisposable
    {
        private readonly Form _form;
        private readonly Thread _thread;

        private TestOverlayWindow(Form form, Thread thread)
        {
            _form = form;
            _thread = thread;
        }

        public IntPtr Handle => _form.Handle;

        public static TestOverlayWindow Start(Point location)
        {
            var ready = new TaskCompletionSource<Form>(TaskCreationOptions.RunContinuationsAsynchronously);
            Form? form = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var createdForm = new Form
                    {
                        FormBorderStyle = FormBorderStyle.None,
                        ShowInTaskbar = false,
                        StartPosition = FormStartPosition.Manual,
                        Location = location,
                        ClientSize = new Size(180, 80),
                        TopMost = true,
                        Text = "NativeSpy test overlay"
                    };
                    form = createdForm;
                    createdForm.Shown += (_, _) => ready.TrySetResult(createdForm);
                    createdForm.Show();
                    Application.Run(createdForm);
                }
                catch (Exception exception)
                {
                    ready.TrySetException(exception);
                }
            })
            {
                IsBackground = true,
                Name = "NativeSpy.IntegrationTests.TestOverlay"
            };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            form = ready.Task.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            _ = SetWindowPos(form.Handle, new IntPtr(-1), location.X, location.Y, 180, 80, 0x0010 | 0x0040);
            _ = SetForegroundWindow(form.Handle);
            return new TestOverlayWindow(form, thread);
        }

        public void Dispose()
        {
            try
            {
                if (!_form.IsDisposed && _form.IsHandleCreated)
                {
                    _form.BeginInvoke(new Action(_form.Close));
                }
            }
            catch
            {
            }

            _thread.Join(TimeSpan.FromSeconds(5));
            _form.Dispose();
        }
    }

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
