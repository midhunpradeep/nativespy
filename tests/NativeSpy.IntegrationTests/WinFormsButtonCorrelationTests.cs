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

        var result = await new CorrelationCoordinator().ResolveUiaToWinFormsAsync(
            source,
            targetPort,
            policy,
            CancellationToken.None);

        Assert.Equal(CorrelationStatus.Exact, result.Status);
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

        var text = target.ReadText(managed.Handle);
        Assert.Equal("NativeSpy Test Button", text);
    }
}
