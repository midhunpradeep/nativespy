using System.Drawing;
using NativeSpy.Protocol.Common;

namespace NativeSpy.FlaUI;

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

    public ulong? CandidateHwnd { get; }

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
