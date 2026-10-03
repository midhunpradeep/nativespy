using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using NativeSpy.Protocol.Common;
using NativeSpy.Protocol.Correlation;

namespace NativeSpy.Agent.WinForms;

/// <summary>
/// Narrow target-side WinForms evidence adapter for the I1 current-HWND path.
/// </summary>
public sealed class WinFormsCurrentHwndAdapter
{
    private readonly Func<object, HandleRefDto> _issueHandle;
    private readonly Func<HandleRefDto, object?> _resolveHandle;
    private readonly Func<Type, string> _typeIdProvider;

    public WinFormsCurrentHwndAdapter(
        Func<object, HandleRefDto> issueHandle,
        Func<HandleRefDto, object?> resolveHandle,
        Func<Type, string> typeIdProvider)
    {
        _issueHandle = issueHandle ?? throw new ArgumentNullException(nameof(issueHandle));
        _resolveHandle = resolveHandle ?? throw new ArgumentNullException(nameof(resolveHandle));
        _typeIdProvider = typeIdProvider ?? throw new ArgumentNullException(nameof(typeIdProvider));
    }

    public FrameworkCorrelationEvidenceDto BeginCurrentHwnd(ulong hwnd)
    {
        try
        {
            var control = FindControl(hwnd);
            var current = IsCurrentControl(control, hwnd);
            var live = IsLiveControl(control, hwnd);
            var candidateTarget = control is null
                ? null
                : CreateCandidateTarget(_issueHandle(control), control.GetType());

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
            var resolvedCandidate = _resolveHandle(candidateHandle);
            var currentControl = FindControl(hwnd);
            var current = IsCurrentControl(currentControl, hwnd);
            var live = IsLiveControl(currentControl, hwnd);
            var referenceEqual = resolvedCandidate is Control candidateControl
                && currentControl is not null
                && ReferenceEquals(candidateControl, currentControl);
            var candidateResolved = resolvedCandidate is not null;

            return CreateEvidence(
                hwnd,
                candidateTarget: null,
                new[]
                {
                    Evidence(
                        "CandidateResolved",
                        candidateResolved ? ProofOutcome.Passed : ProofOutcome.NotAvailable),
                    Evidence(
                        "ControlFromHandleReferenceEqual",
                        candidateResolved && currentControl is not null
                            ? referenceEqual ? ProofOutcome.Passed : ProofOutcome.Failed
                            : ProofOutcome.NotAvailable),
                    Evidence("ControlFromHandle", currentControl is null ? ProofOutcome.Failed : ProofOutcome.Passed),
                    Evidence("CurrentHwndMatches", current ? ProofOutcome.Passed : ProofOutcome.Failed),
                    Evidence("ControlLive", live ? ProofOutcome.Passed : ProofOutcome.Failed)
                },
                new[]
                {
                    Validation(
                        "CandidateResolved",
                        candidateResolved ? ValidationOutcome.Passed : ValidationOutcome.Changed),
                    Validation(
                        "CurrentHwndMatches",
                        current ? ValidationOutcome.Passed : ValidationOutcome.Changed),
                    Validation(
                        "ControlLive",
                        live ? ValidationOutcome.Passed : ValidationOutcome.Changed)
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
        string operationName)
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
            Array.Empty<CorrelationLimitationDto>());
    }

    private FrameworkCorrelationEvidenceDto CreateFailureEvidence(
        ulong hwnd,
        Exception exception,
        string operationName)
    {
        return new FrameworkCorrelationEvidenceDto(
            "winforms",
            Process.GetCurrentProcess().Id,
            candidateTarget: null,
            new[]
            {
                Evidence("ControlFromHandle", ProofOutcome.NotAvailable, exception.Message)
            },
            new[]
            {
                Validation("CurrentHwndMatches", ValidationOutcome.NotAvailable, exception.Message),
                Validation("ControlLive", ValidationOutcome.NotAvailable, exception.Message),
                Validation("CandidateResolved", ValidationOutcome.NotAvailable, exception.Message)
            },
            new CorrelationEffectSummaryDto(
                new[] { EffectCategory.Passive },
                FrameworkStateEffect.None,
                ApplicationCallbackEffect.None,
                Array.Empty<CallbackDetailDto>(),
                VisibleMutationEffect.NotRequested,
                new[] { operationName }),
            new[] { CreateMetadata(hwnd, control: null) },
            new[] { new CorrelationLimitationDto("WinFormsTargetOperationFailed", exception.Message) },
            new OperationErrorDto(OperationErrorCode.TargetOperationFailed, exception.Message));
    }

    private CorrelationTargetRefDto CreateCandidateTarget(
        HandleRefDto handle,
        Type type)
    {
        if (handle.Kind != HandleKind.ClrObject)
        {
            throw new InvalidOperationException("The supplied handle issuer returned a non-ClrObject handle.");
        }

        var boundaryId = handle.BoundaryId
            ?? throw new InvalidOperationException("The supplied handle issuer returned no runtime boundary ID.");
        return new CorrelationTargetRefDto(
            CorrelationTargetKind.ManagedObject,
            managed: new ManagedObjectRefDto(
                handle,
                CreateTypeIdentity(type, boundaryId),
                boundaryId));
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

    private TypeIdentityDto CreateTypeIdentity(Type type, string boundaryId)
    {
        var fullName = type.FullName ?? type.Name;
        var assembly = type.Assembly.GetName();
        var assemblyName = assembly.Name ?? type.Assembly.FullName ?? type.Name;
        var typeId = _typeIdProvider(type);
        if (string.IsNullOrWhiteSpace(typeId))
        {
            throw new InvalidOperationException("The type-ID provider returned an empty session-local type ID.");
        }

        return new TypeIdentityDto(
            typeId,
            fullName,
            assemblyName,
            boundaryId,
            type.IsValueType,
            Array.Empty<TypeRefDto>(),
            Array.Empty<TypeRefDto>(),
            assemblyVersion: assembly.Version?.ToString(),
            moduleVersionId: type.Module.ModuleVersionId.ToString("D"));
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
