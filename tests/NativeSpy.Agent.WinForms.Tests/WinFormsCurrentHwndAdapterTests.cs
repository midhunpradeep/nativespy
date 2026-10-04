using System.Runtime.ExceptionServices;
using System.Windows.Forms;
using NativeSpy.Agent;
using NativeSpy.Agent.WinForms;
using NativeSpy.Protocol.Common;
using NativeSpy.Protocol.Correlation;
using Xunit;
using Xunit.Sdk;

namespace NativeSpy.Agent.WinForms.Tests;

public sealed class WinFormsCurrentHwndAdapterTests
{
    [Fact]
    public void Acquisition_failure_does_not_observe_new_control_facts()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw SkipException.ForSkip("The WinForms adapter test requires Windows.");
        }

        var error = new OperationErrorDto(
            OperationErrorCode.StaleHandle,
            "The handle generation is stale.");
        var handle = new HandleRefDto("session", "handle", 1, HandleKind.ClrObject);
        var evidence = new WinFormsCurrentHwndAdapter(
                new FailingAcquisitionIdentityService(error))
            .RevalidateCurrentHwnd(123, handle);

        Assert.Same(error, evidence.OperationError);
        Assert.Contains("NativeSpy.Agent.TryAcquire", evidence.Effects.Operations);
        Assert.DoesNotContain("WinForms.Control.FromHandle.Revalidate", evidence.Effects.Operations);
        Assert.DoesNotContain(
            evidence.EvidenceFacts,
            fact => fact.Name is "ControlFromHandle" or "CurrentHwndMatches" or "ControlLive");
        Assert.DoesNotContain(
            evidence.ValidationFacts,
            fact => fact.Name is "ControlFromHandle" or "CurrentHwndMatches" or "ControlLive");
    }

    [Fact]
    public void Registration_failure_preserves_observed_control_evidence()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw SkipException.ForSkip("The WinForms adapter test requires Windows.");
        }

        var error = new OperationErrorDto(
            OperationErrorCode.RuntimeUnavailable,
            "The Agent runtime boundary was unavailable.");
        var evidence = RunOnSta(() =>
        {
            using var control = new Button
            {
                Text = "Registration failure test"
            };
            var hwnd = checked((ulong)control.Handle.ToInt64());
            var adapter = new WinFormsCurrentHwndAdapter(new FailingIdentityService(error));
            return adapter.BeginCurrentHwnd(hwnd);
        });

        Assert.Null(evidence.CandidateTarget);
        Assert.Same(error, evidence.OperationError);
        Assert.Contains(
            evidence.Limitations,
            limitation => limitation.Code == "AgentIdentityOperationFailed");
        Assert.DoesNotContain(
            evidence.Limitations,
            limitation => limitation.Code == "WinFormsTargetOperationFailed");
        Assert.Equal(
            ProofOutcome.Passed,
            FindFact(evidence, "ControlFromHandle").Outcome);
        Assert.Equal(
            ProofOutcome.Passed,
            FindFact(evidence, "CurrentHwndMatches").Outcome);
        Assert.Equal(
            ProofOutcome.Passed,
            FindFact(evidence, "ControlLive").Outcome);
        Assert.Equal(
            ValidationOutcome.Passed,
            FindValidation(evidence, "CurrentHwndMatches").Outcome);
        Assert.Equal(
            ValidationOutcome.Passed,
            FindValidation(evidence, "ControlLive").Outcome);
        Assert.Equal(
            ValidationOutcome.NotAvailable,
            FindValidation(evidence, "CandidateResolved").Outcome);
        Assert.Contains("WinForms.Control.FromHandle", evidence.Effects.Operations);

        var metadata = Assert.Single(evidence.AdapterMetadata);
        var properties = metadata.Payload.ObjectValue!;
        Assert.Equal(
            true,
            properties.Single(property => property.Name == "controlFound").Value.BooleanValue);
        Assert.Equal(
            typeof(Button).FullName,
            properties.Single(property => property.Name == "controlType").Value.StringValue);
    }

    private static CorrelationEvidenceFactDto FindFact(
        FrameworkCorrelationEvidenceDto evidence,
        string name)
    {
        return Assert.Single(evidence.EvidenceFacts, fact => fact.Name == name);
    }

    private static CorrelationValidationFactDto FindValidation(
        FrameworkCorrelationEvidenceDto evidence,
        string name)
    {
        return Assert.Single(evidence.ValidationFacts, fact => fact.Name == name);
    }

    private static T RunOnSta<T>(Func<T> callback)
    {
        T result = default!;
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                result = callback();
            }
            catch (Exception caught)
            {
                exception = caught;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (exception is not null)
        {
            ExceptionDispatchInfo.Capture(exception).Throw();
        }

        return result;
    }

    private sealed class FailingIdentityService : IManagedObjectReferenceService
    {
        private readonly OperationErrorDto _error;

        public FailingIdentityService(OperationErrorDto error)
        {
            _error = error;
        }

        public ManagedObjectRegistrationResult Register(object target)
        {
            return ManagedObjectRegistrationResult.Failure(_error);
        }

        public ManagedObjectAcquisitionResult TryAcquire(HandleRefDto handle)
        {
            throw new InvalidOperationException("The registration-only test must not acquire a target.");
        }
    }

    private sealed class FailingAcquisitionIdentityService : IManagedObjectReferenceService
    {
        private readonly OperationErrorDto _error;

        public FailingAcquisitionIdentityService(OperationErrorDto error)
        {
            _error = error;
        }

        public ManagedObjectRegistrationResult Register(object target)
        {
            throw new InvalidOperationException("The acquisition-only test must not register a target.");
        }

        public ManagedObjectAcquisitionResult TryAcquire(HandleRefDto handle)
        {
            return ManagedObjectAcquisitionResult.Failure(_error);
        }
    }
}
