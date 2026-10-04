using System.Security.Cryptography;
using System.Text.Json;
using NativeSpy.Agent;
using NativeSpy.Client.NamedPipes;
using NativeSpy.Agent.Host;
using NativeSpy.Protocol.Common;
using NativeSpy.Protocol.Json;
using NativeSpy.Transport.NamedPipes;
using Xunit;

namespace NativeSpy.IntegrationTests;

public sealed class AgentHostSessionTests
{
    [Fact]
    public async Task Authenticated_session_dispatches_a_request_and_keeps_the_session_identity()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var identity = ProcessIdentityReader.ReadCurrent();
        var nonce = RandomNumberGenerator.GetBytes(32);
        using var options = new AgentHostOptions(
            "nativespy-host-test-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(8)),
            nonce,
            identity,
            NamedPipeSecurity.GetCurrentUserSid(),
            bootstrapLifetime: TimeSpan.FromSeconds(30));
        await using var host = new AgentHost(options, new EchoCompositionFactory());
        await host.StartAsync();

        await using (var unauthenticated = await NamedPipeClient.ConnectAsync(
            new NamedPipeClientOptions(host.BootstrapDescriptor.PipeName),
            TimeSpan.FromSeconds(5)))
        {
            var badHello = new HelloRequestWire
            {
                MinSupportedVersion = 1,
                MaxSupportedVersion = 1,
                ExpectedTargetProcessIdentity = new ProcessIdentityWire
                {
                    ProcessId = identity.ProcessId,
                    ProcessStartIdentity = identity.ProcessStartIdentity
                },
                BootstrapNonce = "wrong-nonce"
            };
            await unauthenticated.WriteFrameAsync(ProtocolJsonCodec.SerializeHelloRequest(badHello));
            using var errorFrame = await unauthenticated.ReadFrameAsync();
            Assert.NotNull(errorFrame);
            var error = ProtocolJsonCodec.DeserializeHandshakeError(errorFrame!.Payload);
            Assert.Equal(ProtocolErrorCode.AuthenticationFailed, error.Code);
        }

        await using var client = await NamedPipeClientSession.ConnectAsync(
            host.BootstrapDescriptor,
            identity,
            cancellationToken: CancellationToken.None);
        using var payloadDocument = JsonDocument.Parse("{\"value\":42}");
        var response = await client.SendRequestAsync("echo", payloadDocument.RootElement);

        Assert.Equal(ProtocolJsonCodec.SuccessStatus, response.ResultStatus);
        Assert.Equal(client.SessionId, response.SessionId);
        Assert.Equal("1", response.RequestId);
        Assert.Equal(42, response.Payload!.Value.GetProperty("value").GetInt32());
    }

    private sealed class EchoCompositionFactory : IAgentHostCompositionFactory
    {
        public IAgentHostComposition Create(
            IManagedObjectReferenceService identityService,
            HostSessionContext context)
        {
            return new EchoComposition();
        }
    }

    private sealed class EchoComposition : IAgentHostComposition
    {
        public IReadOnlyList<IAgentOperationHandler> Handlers { get; } = new[] { new EchoHandler() };

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class EchoHandler : IAgentOperationHandler
    {
        public string OperationName => "echo";

        public Task<AgentHandlerResult> HandleAsync(
            AgentRequestContext context,
            JsonElement payload,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(AgentHandlerResult.Success(payload));
        }
    }
}
