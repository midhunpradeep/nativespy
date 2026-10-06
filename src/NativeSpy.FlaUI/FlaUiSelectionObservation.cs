using System.Drawing;
using NativeSpy.Protocol.Common;

namespace NativeSpy.FlaUI;

/// <summary>
/// Detached UI Automation facts. <see cref="ScreenPoint"/> and <see cref="Bounds"/>
/// are physical desktop pixels, matching Win32 cursor and native window-placement APIs.
/// </summary>
public sealed class FlaUiSelectionObservation
{
    public FlaUiSelectionObservation(
        Point screenPoint,
        int? candidateProcessId,
        ulong? candidateHwnd,
        ulong? rootHwnd,
        Rectangle? bounds,
        string? name,
        string? automationId,
        string? className,
        string? controlType,
        IReadOnlyList<int>? runtimeId,
        bool isAvailable,
        string? limitation = null)
    {
        ScreenPoint = screenPoint;
        CandidateProcessId = candidateProcessId;
        CandidateHwnd = candidateHwnd;
        RootHwnd = rootHwnd;
        Bounds = bounds;
        Name = name;
        AutomationId = automationId;
        ClassName = className;
        ControlType = controlType;
        RuntimeId = runtimeId?.ToArray();
        IsAvailable = isAvailable;
        Limitation = limitation;
    }

    public Point ScreenPoint { get; }

    public int? CandidateProcessId { get; }

    /// <summary>
    /// The native HWND directly exposed by the selected UI Automation element, if any.
    /// HWND-less descendants remain null here.
    /// </summary>
    public ulong? CandidateHwnd { get; }

    /// <summary>
    /// The hosting/root native HWND. This is derived from <see cref="CandidateHwnd"/>
    /// when present, or from the first suitable UI Automation ancestor exposing an HWND.
    /// </summary>
    public ulong? RootHwnd { get; }

    public Rectangle? Bounds { get; }

    public string? Name { get; }

    public string? AutomationId { get; }

    public string? ClassName { get; }

    public string? ControlType { get; }

    public IReadOnlyList<int>? RuntimeId { get; }

    public bool IsAvailable { get; }

    public string? Limitation { get; }
}

public sealed class FlaUiFrozenSelection : IAsyncDisposable
{
    internal FlaUiFrozenSelection(
        FlaUiExternalSource source,
        FlaUiSelectionObservation observation)
    {
        Source = source;
        Observation = observation;
    }

    public FlaUiExternalSource Source { get; }

    public FlaUiSelectionObservation Observation { get; }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
