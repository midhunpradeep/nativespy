using System.Runtime.InteropServices;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using NativeSpy.Client.Correlation;
using NativeSpy.Protocol.Common;
using NativeSpy.Protocol.Correlation;

namespace NativeSpy.FlaUI;

/// <summary>
/// Retains one live FlaUI source internally while exposing detached Client evidence.
/// </summary>
public sealed class FlaUiExternalSource : IExternalUiaObservationPort
{
    private readonly AutomationBase _automation;
    private readonly AutomationElement _element;
    private readonly int _attachedProcessId;
    private readonly ExternalObservationRefDto _source;
    private readonly object _gate = new();
    private ExternalUiaEvidenceDto? _capture;
    private int _equalityCallCount;

    internal FlaUiExternalSource(
        AutomationBase automation,
        AutomationElement element,
        int attachedProcessId)
    {
        _automation = automation ?? throw new ArgumentNullException(nameof(automation));
        _element = element ?? throw new ArgumentNullException(nameof(element));
        if (attachedProcessId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(attachedProcessId), attachedProcessId, "ProcessId must be positive.");
        }

        _attachedProcessId = attachedProcessId;
        _source = new ExternalObservationRefDto(
            $"uia-observation-{Guid.NewGuid():N}",
            $"uia-capture-{Guid.NewGuid():N}");
    }

    public Task<ExternalUiaEvidenceDto> CaptureAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _capture ??= CaptureCore();
            return Task.FromResult(_capture);
        }
    }

    public Task<ExternalUiaEqualityEvidenceDto> CompareWithHwndAsync(
        ulong hwnd,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (hwnd == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(hwnd), hwnd, "HWND must be positive.");
        }

        lock (_gate)
        {
            var capture = _capture
                ?? throw new InvalidOperationException("CaptureAsync must complete before equality comparison.");
            var isRevalidation = Interlocked.Increment(ref _equalityCallCount) > 1;
            return Task.FromResult(CompareCore(capture, hwnd, isRevalidation));
        }
    }

    private ExternalUiaEvidenceDto CaptureCore()
    {
        try
        {
            var nativeWindowHandle = _element.Properties.NativeWindowHandle.ValueOrDefault;
            ulong? observedHwnd = nativeWindowHandle > 0
                ? unchecked((ulong)(uint)nativeWindowHandle)
                : null;
            var elementProcessId = _element.Properties.ProcessId.ValueOrDefault;
            var sourceAvailable = _element.IsAvailable;
            var facts = new List<CorrelationEvidenceFactDto>
            {
                Fact("SourceAvailable", sourceAvailable ? ProofOutcome.Passed : ProofOutcome.Failed),
                Fact(
                    "CurrentHwndObserved",
                    observedHwnd is null ? ProofOutcome.Failed : ProofOutcome.Passed),
                Fact(
                    "ExternalElementProcessIdentity",
                    elementProcessId == _attachedProcessId
                        ? ProofOutcome.Passed
                        : elementProcessId > 0 ? ProofOutcome.Failed : ProofOutcome.NotAvailable)
            };
            AddDescriptiveFact(facts, "AutomationId", _element.Properties.AutomationId.ValueOrDefault);
            AddDescriptiveFact(facts, "Name", _element.Properties.Name.ValueOrDefault);
            AddDescriptiveFact(facts, "ClassName", _element.Properties.ClassName.ValueOrDefault);
            AddDescriptiveFact(facts, "ControlType", _element.Properties.ControlType.ValueOrDefault.ToString());
            return new ExternalUiaEvidenceDto(
                _source,
                _attachedProcessId,
                observedHwnd,
                facts,
                Array.Empty<CorrelationLimitationDto>());
        }
        catch (Exception exception) when (IsExpectedUiaFailure(exception))
        {
            return new ExternalUiaEvidenceDto(
                _source,
                _attachedProcessId,
                observedHwnd: null,
                new[]
                {
                    Fact("SourceAvailable", ProofOutcome.NotAvailable, exception.Message),
                    Fact("CurrentHwndObserved", ProofOutcome.NotAvailable, exception.Message),
                    Fact("ExternalElementProcessIdentity", ProofOutcome.NotAvailable, exception.Message)
                },
                new[] { new CorrelationLimitationDto("ExternalCaptureUnavailable", exception.Message) },
                new OperationErrorDto(OperationErrorCode.TargetOperationFailed, exception.Message));
        }
    }

    private ExternalUiaEqualityEvidenceDto CompareCore(
        ExternalUiaEvidenceDto capture,
        ulong hwnd,
        bool isRevalidation)
    {
        var elementFromHandleName = isRevalidation
            ? "ElementFromHandleRevalidated"
            : "ElementFromHandle";
        var compareName = isRevalidation
            ? "CompareElementsRevalidated"
            : "CompareElements";

        try
        {
            var sourceAvailable = _element.IsAvailable;
            var facts = new List<CorrelationEvidenceFactDto>
            {
                Fact("SourceAvailable", sourceAvailable ? ProofOutcome.Passed : ProofOutcome.Failed)
            };
            if (!sourceAvailable)
            {
                facts.Add(Fact(elementFromHandleName, ProofOutcome.NotAvailable));
                facts.Add(Fact(compareName, ProofOutcome.NotAvailable));
                return new ExternalUiaEqualityEvidenceDto(
                    capture.Source,
                    hwnd,
                    facts,
                    new[] { new CorrelationLimitationDto("ExternalSourceNotAvailable") });
            }

            var current = hwnd <= long.MaxValue
                ? _automation.FromHandle(new IntPtr(unchecked((long)hwnd)))
                : null;
            var elementAvailable = current is not null;
            facts.Add(Fact(
                elementFromHandleName,
                elementAvailable ? ProofOutcome.Passed : ProofOutcome.Failed));
            if (!elementAvailable)
            {
                facts.Add(Fact(compareName, ProofOutcome.NotAvailable));
                return new ExternalUiaEqualityEvidenceDto(
                    capture.Source,
                    hwnd,
                    facts,
                    new[] { new CorrelationLimitationDto("ElementFromHandleUnavailable") });
            }

            var elementsEqual = _automation.Compare(_element, current!);
            facts.Add(Fact(
                compareName,
                elementsEqual ? ProofOutcome.Passed : ProofOutcome.Failed));
            return new ExternalUiaEqualityEvidenceDto(
                capture.Source,
                hwnd,
                facts,
                elementsEqual
                    ? Array.Empty<CorrelationLimitationDto>()
                    : new[] { new CorrelationLimitationDto("CompareElementsMismatch") });
        }
        catch (Exception exception) when (IsExpectedUiaFailure(exception))
        {
            return new ExternalUiaEqualityEvidenceDto(
                capture.Source,
                hwnd,
                new[]
                {
                    Fact("SourceAvailable", ProofOutcome.NotAvailable, exception.Message),
                    Fact(elementFromHandleName, ProofOutcome.NotAvailable, exception.Message),
                    Fact(compareName, ProofOutcome.NotAvailable, exception.Message)
                },
                new[] { new CorrelationLimitationDto("ExternalEqualityUnavailable", exception.Message) },
                new OperationErrorDto(OperationErrorCode.TargetOperationFailed, exception.Message));
        }
    }

    private static CorrelationEvidenceFactDto Fact(
        string name,
        ProofOutcome outcome,
        string? detail = null)
    {
        return new CorrelationEvidenceFactDto(name, outcome, EvidenceKind.Deterministic, detail);
    }

    private static void AddDescriptiveFact(
        List<CorrelationEvidenceFactDto> facts,
        string name,
        string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            facts.Add(new CorrelationEvidenceFactDto(
                name,
                ProofOutcome.Passed,
                EvidenceKind.Descriptive,
                value));
        }
    }

    private static bool IsExpectedUiaFailure(Exception exception)
    {
        return exception is COMException
            or InvalidOperationException
            or TimeoutException
            or ArgumentException;
    }
}
