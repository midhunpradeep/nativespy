using NativeSpy.Client.Correlation;
using NativeSpy.FlaUI;
using NativeSpy.Protocol.Common;
using NativeSpy.Protocol.Correlation;
using Xunit;
using Xunit.Sdk;

namespace NativeSpy.IntegrationTests;

[Collection("NativeSpy live UI")]
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
        Assert.Equal(
            ProtocolOperationNames.ProductionOperations.OrderBy(name => name, StringComparer.Ordinal),
            target.Session.SupportedOperations.OrderBy(name => name, StringComparer.Ordinal));
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

    }
}
