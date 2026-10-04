using System.Security.Cryptography;
using System.Text.Json;
using NativeSpy.Agent;
using NativeSpy.Agent.Host;
using NativeSpy.Client.NamedPipes;
using NativeSpy.Protocol.Common;
using NativeSpy.Protocol.Json;
using NativeSpy.Transport.NamedPipes;
using Xunit;

namespace NativeSpy.IntegrationTests;

public sealed class NamedPipeSessionAdversarialTests
{
    [Fact]
    public async Task Host_timeout_is_authoritative_and_late_work_keeps_the_execution_slot()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var handler = new GateHandler();
        using var options = CreateOptions(
            defaultBudgetMs: 5_000,
            maxBudgetMs: 5_000,
            maximumOutstandingRequests: 1);
        await using var host = new AgentHost(options, new GateCompositionFactory(handler));
        await host.StartAsync();
        await using var client = await NamedPipeClientSession.ConnectAsync(
            host.BootstrapDescriptor,
            options.TargetProcessIdentity,
            cancellationToken: CancellationToken.None);
        using var payload = JsonDocument.Parse("{}");

        var first = await client.SendRequestAsync(
            "test.gate",
            payload.RootElement,
            budgetMs: 25,
            cancellationToken: CancellationToken.None);
        Assert.Equal(ProtocolJsonCodec.OperationErrorStatus, first.ResultStatus);
        Assert.Equal(
            nameof(OperationErrorCode.TargetTimeout),
            first.OperationError?.Code);

        var second = client.SendRequestAsync(
            "test.gate",
            payload.RootElement,
            budgetMs: 5_000,
            cancellationToken: CancellationToken.None);
        await Task.Delay(100);
        Assert.False(second.IsCompleted);

        handler.Release();
        var secondResponse = await second.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(ProtocolJsonCodec.SuccessStatus, secondResponse.ResultStatus);
    }

    [Fact]
    public async Task Caller_abandonment_keeps_the_client_slot_until_the_late_response()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var handler = new GateHandler();
        using var options = CreateOptions(
            defaultBudgetMs: 5_000,
            maxBudgetMs: 5_000,
            maximumOutstandingRequests: 1);
        await using var host = new AgentHost(options, new GateCompositionFactory(handler));
        await host.StartAsync();
        await using var client = await NamedPipeClientSession.ConnectAsync(
            host.BootstrapDescriptor,
            options.TargetProcessIdentity,
            cancellationToken: CancellationToken.None);
        using var payload = JsonDocument.Parse("{}");

        using var cancellation = new CancellationTokenSource();
        var first = client.SendRequestAsync(
            "test.gate",
            payload.RootElement,
            cancellationToken: cancellation.Token);
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);

        var second = client.SendRequestAsync(
            "test.gate",
            payload.RootElement,
            cancellationToken: CancellationToken.None);
        await Task.Delay(100);
        Assert.False(second.IsCompleted);

        handler.Release();
        var response = await second.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(ProtocolJsonCodec.SuccessStatus, response.ResultStatus);
    }

    private static AgentHostOptions CreateOptions(
        ulong defaultBudgetMs,
        ulong maxBudgetMs,
        int maximumOutstandingRequests)
    {
        return new AgentHostOptions(
            "nativespy-session-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(8)),
            RandomNumberGenerator.GetBytes(32),
            ProcessIdentityReader.ReadCurrent(),
            NamedPipeSecurity.GetCurrentUserSid(),
            bootstrapLifetime: TimeSpan.FromSeconds(30),
            defaultBudgetMs: defaultBudgetMs,
            maxBudgetMs: maxBudgetMs,
            maximumOutstandingRequests: maximumOutstandingRequests);
    }

    private sealed class GateCompositionFactory : IAgentHostCompositionFactory
    {
        private readonly GateHandler _handler;

        public GateCompositionFactory(GateHandler handler)
        {
            _handler = handler;
        }

        public IReadOnlyList<string> DeclaredOperationNames { get; } = new[] { "test.gate" };

        public IAgentHostComposition Create(
            IManagedObjectReferenceService identityService,
            HostSessionContext context)
        {
            return new GateComposition(_handler);
        }
    }

    private sealed class GateComposition : IAgentHostComposition
    {
        public GateComposition(GateHandler handler)
        {
            Handlers = new[] { handler };
        }

        public IReadOnlyList<IAgentOperationHandler> Handlers { get; }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class GateHandler : IAgentOperationHandler
    {
        private readonly TaskCompletionSource<object?> _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<object?> Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string OperationName => "test.gate";

        public async Task<AgentHandlerResult> HandleAsync(
            AgentRequestContext context,
            JsonElement payload,
            CancellationToken cancellationToken)
        {
            Started.TrySetResult(null);
            await _release.Task.ConfigureAwait(false);
            return AgentHandlerResult.Success(payload);
        }

        public void Release()
        {
            _release.TrySetResult(null);
        }
    }
}
