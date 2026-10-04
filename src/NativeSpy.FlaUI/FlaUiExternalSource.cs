using System.Runtime.InteropServices;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using NativeSpy.Client.Correlation;
using NativeSpy.Protocol.Common;
using NativeSpy.Protocol.Correlation;

namespace NativeSpy.FlaUI;

/// <summary>
/// Retains one live FlaUI source inside NativeSpy.FlaUI while exposing detached Client evidence.
/// All live UIA calls are scheduled on the owning session's MTA worker.
/// </summary>
public sealed class FlaUiExternalSource : IExternalUiaObservationPort
{
    private readonly FlaUiAutomationSession _session;
    private readonly AutomationBase _automation;
    private readonly AutomationElement _element;
    private readonly int _attachedProcessId;
    private readonly string _observationId;

    internal FlaUiExternalSource(
        FlaUiAutomationSession session,
        AutomationBase automation,
        AutomationElement element,
        int attachedProcessId)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _automation = automation ?? throw new ArgumentNullException(nameof(automation));
        _element = element ?? throw new ArgumentNullException(nameof(element));
        if (attachedProcessId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(attachedProcessId));
        }

        _attachedProcessId = attachedProcessId;
        _observationId = $"uia-observation-{Guid.NewGuid():N}";
    }

    public async Task<ExternalUiaEvidenceDto> CaptureAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var capture = new ExternalUiaCaptureRefDto(
            _observationId,
            $"uia-capture-{Guid.NewGuid():N}");
        try
        {
            return await _session
                .ExecuteAsync(() => CaptureCore(capture), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (IsExpectedUiaFailure(exception))
        {
            return CreateCaptureFailure(capture, exception);
        }
    }

    public async Task<ExternalUiaEqualityEvidenceDto> CompareWithHwndAsync(
        ExternalUiaCaptureRefDto capture,
        ulong hwnd,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(capture);
        cancellationToken.ThrowIfCancellationRequested();
        if (hwnd == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(hwnd));
        }

        if (!string.Equals(capture.ObservationId, _observationId, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The capture reference belongs to a different retained UI Automation observation.",
                nameof(capture));
        }

        try
        {
            return await _session
                .ExecuteAsync(() => CompareCore(capture, hwnd), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (IsExpectedUiaFailure(exception))
        {
            return new ExternalUiaEqualityEvidenceDto(
                capture,
                hwnd,
                new[]
                {
                    Fact("SourceAvailable", ProofOutcome.NotAvailable, exception.Message),
                    Fact("ElementFromHandle", ProofOutcome.NotAvailable, exception.Message),
                    Fact("CompareElements", ProofOutcome.NotAvailable, exception.Message)
                },
                new[] { new CorrelationLimitationDto("ExternalEqualityUnavailable", exception.Message) },
                new OperationErrorDto(OperationErrorCode.TargetOperationFailed, exception.Message));
        }
    }

    private ExternalUiaEvidenceDto CaptureCore(ExternalUiaCaptureRefDto capture)
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
                Fact("CurrentHwndObserved", observedHwnd is null ? ProofOutcome.Failed : ProofOutcome.Passed),
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
                capture,
                _attachedProcessId,
                observedHwnd,
                facts,
                Array.Empty<CorrelationLimitationDto>());
        }
        catch (Exception exception) when (IsExpectedUiaFailure(exception))
        {
            return CreateCaptureFailure(capture, exception);
        }
    }

    private ExternalUiaEqualityEvidenceDto CompareCore(
        ExternalUiaCaptureRefDto capture,
        ulong hwnd)
    {
        try
        {
            var sourceAvailable = _element.IsAvailable;
            var facts = new List<CorrelationEvidenceFactDto>
            {
                Fact("SourceAvailable", sourceAvailable ? ProofOutcome.Passed : ProofOutcome.Failed)
            };
            if (!sourceAvailable)
            {
                facts.Add(Fact("ElementFromHandle", ProofOutcome.NotAvailable));
                facts.Add(Fact("CompareElements", ProofOutcome.NotAvailable));
                return new ExternalUiaEqualityEvidenceDto(
                    capture,
                    hwnd,
                    facts,
                    new[] { new CorrelationLimitationDto("ExternalSourceNotAvailable") });
            }

            var current = hwnd <= long.MaxValue
                ? _automation.FromHandle(new IntPtr(unchecked((long)hwnd)))
                : null;
            var elementAvailable = current is not null;
            facts.Add(Fact("ElementFromHandle", elementAvailable ? ProofOutcome.Passed : ProofOutcome.Failed));
            if (!elementAvailable)
            {
                facts.Add(Fact("CompareElements", ProofOutcome.NotAvailable));
                return new ExternalUiaEqualityEvidenceDto(
                    capture,
                    hwnd,
                    facts,
                    new[] { new CorrelationLimitationDto("ElementFromHandleUnavailable") });
            }

            var elementsEqual = _automation.Compare(_element, current!);
            facts.Add(Fact("CompareElements", elementsEqual ? ProofOutcome.Passed : ProofOutcome.Failed));
            return new ExternalUiaEqualityEvidenceDto(
                capture,
                hwnd,
                facts,
                elementsEqual
                    ? Array.Empty<CorrelationLimitationDto>()
                    : new[] { new CorrelationLimitationDto("CompareElementsMismatch") });
        }
        catch (Exception exception) when (IsExpectedUiaFailure(exception))
        {
            return new ExternalUiaEqualityEvidenceDto(
                capture,
                hwnd,
                new[]
                {
                    Fact("SourceAvailable", ProofOutcome.NotAvailable, exception.Message),
                    Fact("ElementFromHandle", ProofOutcome.NotAvailable, exception.Message),
                    Fact("CompareElements", ProofOutcome.NotAvailable, exception.Message)
                },
                new[] { new CorrelationLimitationDto("ExternalEqualityUnavailable", exception.Message) },
                new OperationErrorDto(OperationErrorCode.TargetOperationFailed, exception.Message));
        }
    }

    private ExternalUiaEvidenceDto CreateCaptureFailure(
        ExternalUiaCaptureRefDto capture,
        Exception exception)
    {
        return new ExternalUiaEvidenceDto(
            capture,
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
            or ArgumentException
            or FlaUiSessionException;
    }
}
