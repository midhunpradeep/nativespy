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

public sealed class AgentHostRequestValidationTests
{
    [Fact]
    public async Task Zero_request_id_is_terminal_and_is_not_dispatched()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var options = CreateOptions();
        await using var host = new AgentHost(options, new ImmediateCompositionFactory());
        await host.StartAsync();
        await using var connection = await AuthenticateRawAsync(host, options.TargetProcessIdentity);
        using var payload = JsonDocument.Parse("{}");

        await connection.WriteFrameAsync(ProtocolJsonCodec.SerializeRequest(new RequestEnvelopeWire
        {
            ProtocolVersion = 1,
            SessionId = host.SessionId!,
            RequestId = "0",
            Operation = "test.echo",
            Payload = payload.RootElement.Clone()
        }));

        using var response = await connection.ReadFrameAsync();
        Assert.Null(response);
        for (var attempt = 0; attempt < 100 && host.State != AgentHostState.Closed; attempt++)
        {
            await Task.Delay(10);
        }

        Assert.Equal(AgentHostState.Closed, host.State);
    }

    [Fact]
    public async Task Duplicate_request_id_returns_protocol_violation_and_terminates_session()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var options = CreateOptions();
        await using var host = new AgentHost(options, new ImmediateCompositionFactory());
        await host.StartAsync();
        await using var connection = await AuthenticateRawAsync(host, options.TargetProcessIdentity);
        using var payload = JsonDocument.Parse("{}");

        async Task SendRequestAsync()
        {
            await connection.WriteFrameAsync(ProtocolJsonCodec.SerializeRequest(new RequestEnvelopeWire
            {
                ProtocolVersion = 1,
                SessionId = host.SessionId!,
                RequestId = "1",
                Operation = "test.echo",
                Payload = payload.RootElement.Clone()
            }));
        }

        await SendRequestAsync();
        using (var firstResponse = await connection.ReadFrameAsync())
        {
            Assert.Equal(ProtocolJsonCodec.SuccessStatus,
                ProtocolJsonCodec.DeserializeResponse(firstResponse!.Payload).ResultStatus);
        }

        await SendRequestAsync();
        using (var violation = await connection.ReadFrameAsync())
        {
            var response = ProtocolJsonCodec.DeserializeResponse(violation!.Payload);
            Assert.Equal(ProtocolJsonCodec.ProtocolErrorStatus, response.ResultStatus);
            Assert.Equal(
                nameof(ProtocolErrorCode.ProtocolViolation),
                response.ProtocolError?.Code);
        }

        for (var attempt = 0; attempt < 100 && host.State != AgentHostState.Closed; attempt++)
        {
            await Task.Delay(10);
        }

        Assert.Equal(AgentHostState.Closed, host.State);
    }

    [Fact]
    public async Task Wrong_session_id_is_a_terminal_protocol_violation()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var options = CreateOptions();
        await using var host = new AgentHost(options, new ImmediateCompositionFactory());
        await host.StartAsync();
        await using var connection = await AuthenticateRawAsync(host, options.TargetProcessIdentity);
        using var payload = JsonDocument.Parse("{}");

        await connection.WriteFrameAsync(ProtocolJsonCodec.SerializeRequest(new RequestEnvelopeWire
        {
            ProtocolVersion = 1,
            SessionId = "different-session",
            RequestId = "1",
            Operation = "test.echo",
            Payload = payload.RootElement.Clone()
        }));

        using var responseFrame = await connection.ReadFrameAsync();
        var response = ProtocolJsonCodec.DeserializeResponse(responseFrame!.Payload);
        Assert.Equal(ProtocolJsonCodec.ProtocolErrorStatus, response.ResultStatus);
        Assert.Equal(nameof(ProtocolErrorCode.ProtocolViolation), response.ProtocolError?.Code);

        for (var attempt = 0; attempt < 100 && host.State != AgentHostState.Closed; attempt++)
        {
            await Task.Delay(10);
        }

        Assert.Equal(AgentHostState.Closed, host.State);
    }

    [Fact]
    public async Task Strictly_increasing_request_ids_may_contain_gaps()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var options = CreateOptions();
        await using var host = new AgentHost(options, new ImmediateCompositionFactory());
        await host.StartAsync();
        await using var connection = await AuthenticateRawAsync(host, options.TargetProcessIdentity);
        using var payload = JsonDocument.Parse("{}");

        foreach (var requestId in new[] { "2", "4" })
        {
            await connection.WriteFrameAsync(ProtocolJsonCodec.SerializeRequest(new RequestEnvelopeWire
            {
                ProtocolVersion = 1,
                SessionId = host.SessionId!,
                RequestId = requestId,
                Operation = "test.echo",
                Payload = payload.RootElement.Clone()
            }));
            using var response = await connection.ReadFrameAsync();
            Assert.Equal(
                ProtocolJsonCodec.SuccessStatus,
                ProtocolJsonCodec.DeserializeResponse(response!.Payload).ResultStatus);
        }
    }

    [Fact]
    public async Task Unknown_operation_is_a_nonterminal_protocol_error()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var options = CreateOptions();
        await using var host = new AgentHost(options, new ImmediateCompositionFactory());
        await host.StartAsync();
        await using var client = await NamedPipeClientSession.ConnectAsync(
            host.BootstrapDescriptor,
            options.TargetProcessIdentity,
            cancellationToken: CancellationToken.None);
        using var payload = JsonDocument.Parse("{}");

        var response = await client.SendRequestAsync(
            "test.unknown",
            payload.RootElement,
            cancellationToken: CancellationToken.None);

        Assert.Equal(ProtocolJsonCodec.ProtocolErrorStatus, response.ResultStatus);
        Assert.Equal(
            nameof(ProtocolErrorCode.UnsupportedOperation),
            response.ProtocolError?.Code);
        Assert.Equal(AgentHostState.Active, host.State);
    }

    private static AgentHostOptions CreateOptions()
    {
        return new AgentHostOptions(
            "nativespy-request-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(8)),
            RandomNumberGenerator.GetBytes(32),
            ProcessIdentityReader.ReadCurrent(),
            NamedPipeSecurity.GetCurrentUserSid(),
            bootstrapLifetime: TimeSpan.FromSeconds(30));
    }

    private static async Task<NamedPipeConnection> AuthenticateRawAsync(
        AgentHost host,
        ProcessIdentityDto identity)
    {
        var connection = await NamedPipeClient.ConnectAsync(
            new NamedPipeClientOptions(host.BootstrapDescriptor.PipeName),
            TimeSpan.FromSeconds(5));
        try
        {
            await connection.WriteFrameAsync(ProtocolJsonCodec.SerializeHelloRequest(new HelloRequestWire
            {
                MinSupportedVersion = 1,
                MaxSupportedVersion = 1,
                ExpectedTargetProcessIdentity = new ProcessIdentityWire
                {
                    ProcessId = identity.ProcessId,
                    ProcessStartIdentity = identity.ProcessStartIdentity
                },
                BootstrapNonce = host.BootstrapDescriptor.BootstrapNonce
            }));
            using var response = await connection.ReadFrameAsync()
                ?? throw new InvalidOperationException("The Host closed during raw authentication.");
            var hello = ProtocolJsonCodec.DeserializeHelloResponse(response.Payload);
            hello.EnsureMessageKind();
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
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
