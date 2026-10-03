using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;

namespace NativeSpy.FlaUI;

/// <summary>
/// Small UIA client session used by the I1 walking skeleton.
/// </summary>
public sealed class FlaUiAutomationSession : IDisposable
{
    private readonly Application _application;
    private readonly UIA3Automation _automation;
    private bool _disposed;

    private FlaUiAutomationSession(
        Application application,
        UIA3Automation automation,
        int processId)
    {
        _application = application;
        _automation = automation;
        ProcessId = processId;
    }

    public int ProcessId { get; }

    public static FlaUiAutomationSession Attach(int processId)
    {
        if (processId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(processId), processId, "ProcessId must be positive.");
        }

        return new FlaUiAutomationSession(
            Application.Attach(processId),
            new UIA3Automation(),
            processId);
    }

    public FlaUiExternalSource FindByAutomationId(string automationId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (string.IsNullOrWhiteSpace(automationId))
        {
            throw new ArgumentException("AutomationId must not be blank.", nameof(automationId));
        }

        var window = _application.GetMainWindow(_automation, TimeSpan.FromSeconds(10))
            ?? throw new InvalidOperationException("The target process did not expose a main UI Automation window.");
        var element = window.FindFirstDescendant(automationId)
            ?? throw new InvalidOperationException($"No UI Automation element matched AutomationId '{automationId}'.");
        return new FlaUiExternalSource(_automation, element, ProcessId);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _automation.Dispose();
        _application.Dispose();
    }
}
