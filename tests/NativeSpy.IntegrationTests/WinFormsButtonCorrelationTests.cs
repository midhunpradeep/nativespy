using NativeSpy.Client.Correlation;
using NativeSpy.FlaUI;
using NativeSpy.Protocol.Common;
using NativeSpy.Protocol.Correlation;
using Xunit;
using Xunit.Sdk;

namespace NativeSpy.IntegrationTests;

public sealed class WinFormsButtonCorrelationTests
{
    [Fact]
    public async Task Real_fla_ui_button_resolves_to_the_actual_clr_button()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw SkipException.ForSkip("The I1 live UI Automation test requires Windows.");
        }

        using var target = TestTargetProcess.Start();
        using var flaUi = FlaUiAutomationSession.Attach(target.ProcessId);
        var source = flaUi.FindByAutomationId("NativeSpyTestButton");
        var targetPort = new TestTargetWinFormsPort(target);
        var policy = new CorrelationPolicyDto(
            CorrelationPolicyMode.Conservative,
            maxCandidates: 1,
            maxExternalNodes: 2_000,
            maxProviderNodes: 32);

        var coordinator = new CorrelationCoordinator();
        var result = await coordinator.ResolveUiaToWinFormsAsync(
            source,
            targetPort,
            policy,
            CancellationToken.None);
        var secondResult = await coordinator.ResolveUiaToWinFormsAsync(
            source,
            targetPort,
            policy,
            CancellationToken.None);

        Assert.Equal(CorrelationStatus.Exact, result.Status);
        Assert.Equal(CorrelationStatus.Exact, secondResult.Status);
        Assert.Equal(
            result.Source.ExternalObservation!.ObservationId,
            secondResult.Source.ExternalObservation!.ObservationId);
        Assert.NotEqual(
            result.Source.ExternalObservation.CaptureId,
            secondResult.Source.ExternalObservation.CaptureId);
        var candidate = Assert.Single(result.Candidates);
        Assert.NotNull(result.PrimaryCandidateId);
        Assert.Equal(candidate.CandidateId, result.PrimaryCandidateId);
        Assert.Equal(CorrelationTargetKind.ManagedObject, candidate.Target.TargetKind);
        var managed = Assert.IsType<ManagedObjectRefDto>(candidate.Target.Managed);
        Assert.Equal(HandleKind.ClrObject, managed.Handle.Kind);
        Assert.Contains(
            candidate.Relationships,
            relationship => relationship.Kind == RelationshipKind.SameManagedElement);
        Assert.Equal(
            "System.Windows.Forms.Button",
            managed.TypeIdentity?.FullName);
        Assert.Equal(managed.Handle.BoundaryId, managed.TypeIdentity?.BoundaryId);
        Assert.False(string.IsNullOrWhiteSpace(managed.TypeIdentity?.TypeId));
        Assert.NotEqual(managed.TypeIdentity?.FullName, managed.TypeIdentity?.TypeId);
        Assert.NotEqual(
            typeof(System.Windows.Forms.Button).AssemblyQualifiedName,
            managed.TypeIdentity?.TypeId);

        var secondManaged = Assert.IsType<ManagedObjectRefDto>(
            Assert.Single(secondResult.Candidates).Target.Managed);
        Assert.Equal(managed.Handle.SessionId, secondManaged.Handle.SessionId);
        Assert.Equal(managed.Handle.HandleId, secondManaged.Handle.HandleId);
        Assert.Equal(managed.Handle.Generation, secondManaged.Handle.Generation);
        Assert.Equal(managed.Handle.Kind, secondManaged.Handle.Kind);
        Assert.Equal(managed.Handle.BoundaryId, secondManaged.Handle.BoundaryId);
        Assert.Equal(managed.TypeIdentity?.TypeId, secondManaged.TypeIdentity?.TypeId);

        var formEvidence = target.Begin(target.MainWindowHwnd);
        var formManaged = Assert.IsType<ManagedObjectRefDto>(formEvidence.CandidateTarget?.Managed);
        Assert.NotEqual(managed.TypeIdentity?.TypeId, formManaged.TypeIdentity?.TypeId);
        Assert.Equal(formManaged.Handle.BoundaryId, formManaged.TypeIdentity?.BoundaryId);
        Assert.Contains(
            formEvidence.EvidenceFacts,
            fact => fact.Name == "ControlFromHandle" && fact.Outcome == ProofOutcome.Passed);
        Assert.Contains("WinForms.Control.FromHandle", formEvidence.Effects.Operations);
        Assert.NotEmpty(formEvidence.AdapterMetadata);
        Assert.Empty(formEvidence.Limitations);
        Assert.Null(formEvidence.OperationError);

        var forgedGeneration = new HandleRefDto(
            managed.Handle.SessionId,
            managed.Handle.HandleId,
            managed.Handle.Generation + 1,
            managed.Handle.Kind,
            managed.Handle.BoundaryId);
        var forgedException = Assert.Throws<TestTargetOperationException>(
            () => target.ReadText(forgedGeneration));
        Assert.Equal(OperationErrorCode.StaleHandle, forgedException.Code);

        var text = target.ReadText(managed.Handle);
        Assert.Equal("NativeSpy Test Button", text);
    }
}
