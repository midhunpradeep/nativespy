using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using NativeSpy.Agent;
using NativeSpy.Protocol.Common;
using NativeSpy.Protocol.Correlation;

namespace NativeSpy.Agent.WinForms;

/// <summary>
/// Narrow target-side WinForms evidence adapter for the I1 current-HWND path.
/// </summary>
public sealed class WinFormsCurrentHwndAdapter
{
    private readonly IManagedObjectReferenceService _identityService;

    public WinFormsCurrentHwndAdapter(IManagedObjectReferenceService identityService)
    {
        _identityService = identityService ?? throw new ArgumentNullException(nameof(identityService));
    }

    public FrameworkCorrelationEvidenceDto BeginCurrentHwnd(ulong hwnd)
    {
        try
        {
            var control = FindControl(hwnd);
            var current = IsCurrentControl(control, hwnd);
            var live = IsLiveControl(control, hwnd);
            CorrelationTargetRefDto? candidateTarget = null;
            if (control is not null)
            {
                var registration = _identityService.Register(control);
                if (!registration.IsSuccess)
                {
                    return CreateRegistrationFailureEvidence(
                        hwnd,
                        registration.Error ?? throw new InvalidOperationException(
                            "The identity service returned an invalid registration result."),
                        control,
                        current,
                        live);
                }

                candidateTarget = new CorrelationTargetRefDto(
                    CorrelationTargetKind.ManagedObject,
                    managed: registration.Reference);
            }

            return CreateEvidence(
                hwnd,
                candidateTarget,
                new[]
                {
                    Evidence("ControlFromHandle", control is null ? ProofOutcome.Failed : ProofOutcome.Passed),
                    Evidence("CurrentHwndMatches", current ? ProofOutcome.Passed : ProofOutcome.Failed),
                    Evidence("ControlLive", live ? ProofOutcome.Passed : ProofOutcome.Failed)
                },
                new[]
                {
                    Validation("CurrentHwndMatches", current ? ValidationOutcome.Passed : ValidationOutcome.Failed),
                    Validation("ControlLive", live ? ValidationOutcome.Passed : ValidationOutcome.Failed)
                },
                control,
                operationName: "WinForms.Control.FromHandle");
        }
        catch (Exception exception) when (IsExpectedAdapterFailure(exception))
        {
            return CreateFailureEvidence(hwnd, exception, "WinForms.Control.FromHandle");
        }
    }

    public FrameworkCorrelationEvidenceDto RevalidateCurrentHwnd(
        ulong hwnd,
        HandleRefDto candidateHandle)
    {
        ArgumentNullException.ThrowIfNull(candidateHandle);

        try
        {
            var acquisitionResult = _identityService.TryAcquire(candidateHandle);
            if (!acquisitionResult.IsSuccess)
            {
                return CreateAcquisitionFailureEvidence(
                    hwnd,
                    acquisitionResult.Error ?? throw new InvalidOperationException(
                        "The identity service returned an invalid acquisition result."));
            }

            using var acquisition = acquisitionResult.Acquisition
                ?? throw new InvalidOperationException(
                    "The identity service returned no acquisition for a successful result.");
            var resolvedCandidate = acquisition.Target;
            var currentControl = FindControl(hwnd);
            var current = IsCurrentControl(currentControl, hwnd);
            var live = IsLiveControl(currentControl, hwnd);
            var referenceEqual = currentControl is not null
                && ReferenceEquals(resolvedCandidate, currentControl);

            return CreateEvidence(
                hwnd,
                candidateTarget: null,
                new[]
                {
                    Evidence("CandidateResolved", ProofOutcome.Passed),
                    Evidence(
                        "ControlFromHandleReferenceEqual",
                        currentControl is not null
                            ? referenceEqual ? ProofOutcome.Passed : ProofOutcome.Failed
                            : ProofOutcome.NotAvailable),
                    Evidence("ControlFromHandle", currentControl is null ? ProofOutcome.Failed : ProofOutcome.Passed),
                    Evidence("CurrentHwndMatches", current ? ProofOutcome.Passed : ProofOutcome.Failed),
                    Evidence("ControlLive", live ? ProofOutcome.Passed : ProofOutcome.Failed)
                },
                new[]
                {
                    Validation("CandidateResolved", ValidationOutcome.Passed),
                    Validation("CurrentHwndMatches", current ? ValidationOutcome.Passed : ValidationOutcome.Changed),
                    Validation("ControlLive", live ? ValidationOutcome.Passed : ValidationOutcome.Changed)
                },
                currentControl,
                operationName: "WinForms.Control.FromHandle.Revalidate");
        }
        catch (Exception exception) when (IsExpectedAdapterFailure(exception))
        {
            return CreateFailureEvidence(hwnd, exception, "WinForms.Control.FromHandle.Revalidate");
        }
    }

    private FrameworkCorrelationEvidenceDto CreateEvidence(
        ulong hwnd,
        CorrelationTargetRefDto? candidateTarget,
        IEnumerable<CorrelationEvidenceFactDto> evidenceFacts,
        IEnumerable<CorrelationValidationFactDto> validationFacts,
        Control? control,
        string operationName,
        OperationErrorDto? operationError = null,
        IEnumerable<CorrelationLimitationDto>? limitations = null)
    {
        return new FrameworkCorrelationEvidenceDto(
            "winforms",
            Process.GetCurrentProcess().Id,
            candidateTarget,
            evidenceFacts,
            validationFacts,
            new CorrelationEffectSummaryDto(
                new[] { EffectCategory.Passive },
                FrameworkStateEffect.None,
                ApplicationCallbackEffect.None,
                Array.Empty<CallbackDetailDto>(),
                VisibleMutationEffect.NotRequested,
                new[] { operationName }),
            new[] { CreateMetadata(hwnd, control) },
            limitations ?? Array.Empty<CorrelationLimitationDto>(),
            operationError);
    }

    private FrameworkCorrelationEvidenceDto CreateFailureEvidence(
        ulong hwnd,
        Exception exception,
        string operationName)
    {
        return CreateFailureEvidence(
            hwnd,
            new OperationErrorDto(OperationErrorCode.TargetOperationFailed, exception.Message),
            operationName);
    }

    private FrameworkCorrelationEvidenceDto CreateRegistrationFailureEvidence(
        ulong hwnd,
        OperationErrorDto operationError,
        Control control,
        bool current,
        bool live)
    {
        return CreateEvidence(
            hwnd,
            candidateTarget: null,
            new[]
            {
                Evidence("ControlFromHandle", ProofOutcome.Passed),
                Evidence("CurrentHwndMatches", current ? ProofOutcome.Passed : ProofOutcome.Failed),
                Evidence("ControlLive", live ? ProofOutcome.Passed : ProofOutcome.Failed)
            },
            new[]
            {
                Validation("CurrentHwndMatches", current ? ValidationOutcome.Passed : ValidationOutcome.Failed),
                Validation("ControlLive", live ? ValidationOutcome.Passed : ValidationOutcome.Failed),
                Validation("CandidateResolved", ValidationOutcome.NotAvailable, operationError.Message)
            },
            control,
            operationName: "WinForms.Control.FromHandle",
            operationError: operationError,
            limitations: new[]
            {
                new CorrelationLimitationDto(
                    "AgentIdentityOperationFailed",
                    operationError.Message)
            });
    }

    private FrameworkCorrelationEvidenceDto CreateFailureEvidence(
        ulong hwnd,
        OperationErrorDto operationError,
        string operationName)
    {
        return new FrameworkCorrelationEvidenceDto(
            "winforms",
            Process.GetCurrentProcess().Id,
            candidateTarget: null,
            new[]
            {
                Evidence("ControlFromHandle", ProofOutcome.NotAvailable, operationError.Message)
            },
            new[]
            {
                Validation("CurrentHwndMatches", ValidationOutcome.NotAvailable, operationError.Message),
                Validation("ControlLive", ValidationOutcome.NotAvailable, operationError.Message),
                Validation("CandidateResolved", ValidationOutcome.NotAvailable, operationError.Message)
            },
            new CorrelationEffectSummaryDto(
                new[] { EffectCategory.Passive },
                FrameworkStateEffect.None,
                ApplicationCallbackEffect.None,
                Array.Empty<CallbackDetailDto>(),
                VisibleMutationEffect.NotRequested,
                new[] { operationName }),
            new[] { CreateMetadata(hwnd, control: null) },
            new[]
            {
                new CorrelationLimitationDto(
                    operationError.Code == OperationErrorCode.TargetOperationFailed
                        ? "WinFormsTargetOperationFailed"
                        : "AgentIdentityOperationFailed",
                    operationError.Message)
            },
            operationError);
    }

    private FrameworkCorrelationEvidenceDto CreateAcquisitionFailureEvidence(
        ulong hwnd,
        OperationErrorDto operationError)
    {
        var candidateResolution = operationError.Code is
            OperationErrorCode.InvalidHandle
            or OperationErrorCode.StaleHandle
            or OperationErrorCode.ObjectCollected
            ? ValidationOutcome.Changed
            : ValidationOutcome.NotAvailable;
        return new FrameworkCorrelationEvidenceDto(
            "winforms",
            Process.GetCurrentProcess().Id,
            candidateTarget: null,
            new[]
            {
                Evidence("CandidateResolved", ProofOutcome.NotAvailable, operationError.Message),
                Evidence("ControlFromHandleReferenceEqual", ProofOutcome.NotAvailable, operationError.Message)
            },
            new[]
            {
                Validation("CandidateResolved", candidateResolution, operationError.Message)
            },
            new CorrelationEffectSummaryDto(
                new[] { EffectCategory.Passive },
                FrameworkStateEffect.None,
                ApplicationCallbackEffect.None,
                Array.Empty<CallbackDetailDto>(),
                VisibleMutationEffect.NotRequested,
                new[] { "NativeSpy.Agent.TryAcquire" }),
            Array.Empty<AdapterMetadataDto>(),
            Array.Empty<CorrelationLimitationDto>(),
            operationError);
    }

    private static Control? FindControl(ulong hwnd)
    {
        if (hwnd == 0 || hwnd > long.MaxValue)
        {
            return null;
        }

        return Control.FromHandle(new IntPtr(unchecked((long)hwnd)));
    }

    private static bool IsCurrentControl(Control? control, ulong hwnd)
    {
        if (control is null || !control.IsHandleCreated || control.IsDisposed || control.Disposing)
        {
            return false;
        }

        return unchecked((ulong)control.Handle.ToInt64()) == hwnd;
    }

    private static bool IsLiveControl(Control? control, ulong hwnd)
    {
        return IsCurrentControl(control, hwnd);
    }

    private static AdapterMetadataDto CreateMetadata(ulong hwnd, Control? control)
    {
        var properties = new List<DetachedMetadataPropertyDto>
        {
            new("observedHwnd", DetachedMetadataValueDto.Integer(unchecked((long)hwnd))),
            new("uiThreadId", DetachedMetadataValueDto.Integer(Environment.CurrentManagedThreadId)),
            new("controlFound", DetachedMetadataValueDto.Boolean(control is not null)),
            new("isHandleCreated", DetachedMetadataValueDto.Boolean(control?.IsHandleCreated == true)),
            new("isDisposed", DetachedMetadataValueDto.Boolean(control?.IsDisposed == true)),
            new("isDisposing", DetachedMetadataValueDto.Boolean(control?.Disposing == true))
        };
        if (control is not null)
        {
            properties.Add(new DetachedMetadataPropertyDto(
                "controlType",
                DetachedMetadataValueDto.String(control.GetType().FullName ?? control.GetType().Name)));
        }

        return new AdapterMetadataDto(
            "winforms",
            "current-hwnd",
            1,
            DetachedMetadataValueDto.Object(properties));
    }

    private static CorrelationEvidenceFactDto Evidence(
        string name,
        ProofOutcome outcome,
        string? detail = null)
    {
        return new CorrelationEvidenceFactDto(name, outcome, EvidenceKind.Deterministic, detail);
    }

    private static CorrelationValidationFactDto Validation(
        string name,
        ValidationOutcome outcome,
        string? detail = null)
    {
        return new CorrelationValidationFactDto(name, outcome, detail);
    }

    private static bool IsExpectedAdapterFailure(Exception exception)
    {
        return exception is ArgumentException
            or InvalidOperationException
            or ObjectDisposedException
            or ExternalException;
    }
}
