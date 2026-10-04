using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NativeSpy.Agent;
using NativeSpy.Agent.Host;
using NativeSpy.Client.NamedPipes;
using NativeSpy.Protocol.Common;
using NativeSpy.Protocol.Json;
using NativeSpy.Transport.NamedPipes;
using Xunit;

namespace NativeSpy.IntegrationTests;

public sealed class AgentHostAuthenticationTests
{
    [Fact]
    public async Task Invalid_hello_does_not_close_listener_or_consume_nonce()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var options = CreateOptions();
        await using var host = new AgentHost(options, new ImmediateCompositionFactory());
        await host.StartAsync();
        await using (var connection = await NamedPipeClient.ConnectAsync(
            new NamedPipeClientOptions(host.BootstrapDescriptor.PipeName),
            TimeSpan.FromSeconds(5)))
        {
            await connection.WriteFrameAsync(Encoding.UTF8.GetBytes("{}"));
            using var frame = await connection.ReadFrameAsync();
            var error = ProtocolJsonCodec.DeserializeHandshakeError(frame!.Payload);
            Assert.Equal(ProtocolErrorCode.InvalidRequest, error.Code);
        }

        await using var client = await NamedPipeClientSession.ConnectAsync(
            host.BootstrapDescriptor,
            options.TargetProcessIdentity,
            cancellationToken: CancellationToken.None);
        for (var attempt = 0; attempt < 100 && host.State != AgentHostState.Active; attempt++)
        {
            await Task.Delay(10);
        }

        Assert.Equal(AgentHostState.Active, host.State);
    }

    [Fact]
    public async Task Wrong_independent_process_identity_is_rejected_without_consuming_nonce()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var options = CreateOptions();
        await using var host = new AgentHost(options, new ImmediateCompositionFactory());
        await host.StartAsync();
        await using var connection = await NamedPipeClient.ConnectAsync(
            new NamedPipeClientOptions(host.BootstrapDescriptor.PipeName),
            TimeSpan.FromSeconds(5));

        var hello = CreateHello(
            host,
            new ProcessIdentityDto(
                options.TargetProcessIdentity.ProcessId,
                "wrong-process-instance"),
            1,
            1);
        await connection.WriteFrameAsync(ProtocolJsonCodec.SerializeHelloRequest(hello));
        using var frame = await connection.ReadFrameAsync();
        var error = ProtocolJsonCodec.DeserializeHandshakeError(frame!.Payload);

        Assert.Equal(ProtocolErrorCode.AuthenticationFailed, error.Code);
        Assert.Equal(32, options.BootstrapNonceBytes.Length);
    }

    [Fact]
    public async Task Version_mismatch_is_rejected_without_consuming_nonce()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var options = CreateOptions();
        await using var host = new AgentHost(options, new ImmediateCompositionFactory());
        await host.StartAsync();
        await using var connection = await NamedPipeClient.ConnectAsync(
            new NamedPipeClientOptions(host.BootstrapDescriptor.PipeName),
            TimeSpan.FromSeconds(5));

        var hello = CreateHello(host, options.TargetProcessIdentity, 2, 2);
        await connection.WriteFrameAsync(ProtocolJsonCodec.SerializeHelloRequest(hello));
        using var frame = await connection.ReadFrameAsync();
        var error = ProtocolJsonCodec.DeserializeHandshakeError(frame!.Payload);

        Assert.Equal(ProtocolErrorCode.ProtocolMismatch, error.Code);
        Assert.Equal(32, options.BootstrapNonceBytes.Length);
    }

    [Fact]
    public async Task Active_session_is_single_client_and_never_reconnects()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var options = CreateOptions();
        await using var host = new AgentHost(options, new ImmediateCompositionFactory());
        await host.StartAsync();
        await using var first = await NamedPipeClientSession.ConnectAsync(
            host.BootstrapDescriptor,
            options.TargetProcessIdentity,
            cancellationToken: CancellationToken.None);

        await Assert.ThrowsAnyAsync<Exception>(() =>
            NamedPipeClient.ConnectAsync(
                new NamedPipeClientOptions(host.BootstrapDescriptor.PipeName),
                TimeSpan.FromMilliseconds(100)));

        await first.DisposeAsync();
        for (var attempt = 0; attempt < 100 && host.State != AgentHostState.Closed; attempt++)
        {
            await Task.Delay(10);
        }

        Assert.Equal(AgentHostState.Closed, host.State);
        await Assert.ThrowsAnyAsync<Exception>(() =>
            NamedPipeClient.ConnectAsync(
                new NamedPipeClientOptions(host.BootstrapDescriptor.PipeName),
                TimeSpan.FromMilliseconds(100)));
    }

    [Fact]
    public async Task Bootstrap_expiry_closes_the_host_and_clears_nonce()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var options = new AgentHostOptions(
            "nativespy-expiry-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(8)),
            RandomNumberGenerator.GetBytes(32),
            ProcessIdentityReader.ReadCurrent(),
            NamedPipeSecurity.GetCurrentUserSid(),
            bootstrapLifetime: TimeSpan.FromMilliseconds(50));
        await using var host = new AgentHost(options, new ImmediateCompositionFactory());
        await host.StartAsync();

        for (var attempt = 0; attempt < 100 && host.State != AgentHostState.Closed; attempt++)
        {
            await Task.Delay(10);
        }

        Assert.Equal(AgentHostState.Closed, host.State);
        Assert.Throws<ObjectDisposedException>(() => _ = options.BootstrapNonceBytes);
    }

    private static AgentHostOptions CreateOptions()
    {
        return new AgentHostOptions(
            "nativespy-auth-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(8)),
            RandomNumberGenerator.GetBytes(32),
            ProcessIdentityReader.ReadCurrent(),
            NamedPipeSecurity.GetCurrentUserSid(),
            bootstrapLifetime: TimeSpan.FromSeconds(30));
    }

    private static HelloRequestWire CreateHello(
        AgentHost host,
        ProcessIdentityDto identity,
        int minVersion,
        int maxVersion)
    {
        return new HelloRequestWire
        {
            MinSupportedVersion = minVersion,
            MaxSupportedVersion = maxVersion,
            ExpectedTargetProcessIdentity = new ProcessIdentityWire
            {
                ProcessId = identity.ProcessId,
                ProcessStartIdentity = identity.ProcessStartIdentity
            },
            BootstrapNonce = host.BootstrapDescriptor.BootstrapNonce
        };
    }

    private sealed class ImmediateCompositionFactory : IAgentHostCompositionFactory
    {
        public IReadOnlyList<string> DeclaredOperationNames { get; } = new[] { "test.echo" };

        public IAgentHostComposition Create(
            IManagedObjectReferenceService identityService,
            HostSessionContext context)
        {
            return new ImmediateComposition();
        }
    }

    private sealed class ImmediateComposition : IAgentHostComposition
    {
        public IReadOnlyList<IAgentOperationHandler> Handlers { get; } = new[] { new ImmediateHandler() };

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ImmediateHandler : IAgentOperationHandler
    {
        public string OperationName => "test.echo";

        public Task<AgentHandlerResult> HandleAsync(
            AgentRequestContext context,
            JsonElement payload,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(AgentHandlerResult.Success(payload));
        }
    }
}
