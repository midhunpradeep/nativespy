using System.Security.Cryptography;
using System.Text.Json;
using NativeSpy.Agent;
using NativeSpy.Agent.Host;
using NativeSpy.Client.NamedPipes;
using NativeSpy.Protocol.Clr;
using NativeSpy.Protocol.Common;
using NativeSpy.Protocol.Correlation;
using NativeSpy.Protocol.Json;
using NativeSpy.Transport.NamedPipes;
using Xunit;

namespace NativeSpy.IntegrationTests;

public sealed class NamedPipeClientMalformedClrResponseTests
{
    [Theory]
    [InlineData("clr.describeObject")]
    [InlineData("clr.listMembers")]
    [InlineData("clr.readFieldValues")]
    [InlineData("clr.readPropertyValue")]
    public async Task Malformed_clr_success_payloads_become_terminal_protocol_failures(string operation)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var options = CreateOptions();
        await using var host = new AgentHost(
            options,
            new MalformedResponseCompositionFactory(operation));
        await host.StartAsync();
        await using var client = await NamedPipeClientSession.ConnectAsync(
            host.BootstrapDescriptor,
            options.TargetProcessIdentity,
            cancellationToken: CancellationToken.None);

        var exception = await Assert.ThrowsAsync<NamedPipeProtocolException>(() => InvokeAsync(client, operation));

        Assert.Equal(ProtocolErrorCode.ProtocolViolation, exception.Error.Code);
        Assert.Contains("invalid CLR success payload", exception.Message, StringComparison.OrdinalIgnoreCase);
        await Assert.ThrowsAsync<NamedPipeProtocolException>(() => InvokeAsync(client, operation));
    }

    private static async Task InvokeAsync(NamedPipeClientSession client, string operation)
    {
        var @object = CreateObject();
        var member = new MemberRefDto("session", "member", "boundary", "type");
        switch (operation)
        {
            case "clr.describeObject":
                await client.DescribeObjectAsync(@object, CancellationToken.None);
                break;
            case "clr.listMembers":
                await client.ListMembersAsync(
                    @object,
                    1,
                    ClrMemberKindFilter.All,
                    continuationToken: null,
                    CancellationToken.None);
                break;
            case "clr.readFieldValues":
                await client.ReadFieldValuesAsync(@object, new[] { member }, CancellationToken.None);
                break;
            case "clr.readPropertyValue":
                await client.ReadPropertyValueAsync(@object, member, CancellationToken.None);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(operation));
        }
    }

    private static AgentHostOptions CreateOptions()
    {
        return new AgentHostOptions(
            "nativespy-malformed-clr-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(8)),
            RandomNumberGenerator.GetBytes(32),
            ProcessIdentityReader.ReadCurrent(),
            NamedPipeSecurity.GetCurrentUserSid(),
            bootstrapLifetime: TimeSpan.FromSeconds(30));
    }

    private static ManagedObjectRefDto CreateObject()
    {
        var type = new TypeIdentityDto(
            "type",
            "Example.Type",
            "Example",
            "boundary",
            isValueType: false,
            Array.Empty<TypeRefDto>(),
            Array.Empty<TypeRefDto>());
        return new ManagedObjectRefDto(
            new HandleRefDto("session", "object", 1, HandleKind.ClrObject, "boundary"),
            type,
            "boundary");
    }

    private sealed class MalformedResponseCompositionFactory : IAgentHostCompositionFactory
    {
        private readonly string _operation;

        public MalformedResponseCompositionFactory(string operation)
        {
            _operation = operation;
        }

        public IReadOnlyList<string> DeclaredOperationNames => new[] { _operation };

        public IAgentHostComposition Create(
            IManagedObjectReferenceService identityService,
            HostSessionContext context)
        {
            return new MalformedResponseComposition(_operation);
        }
    }

    private sealed class MalformedResponseComposition : IAgentHostComposition
    {
        public MalformedResponseComposition(string operation)
        {
            Handlers = new[] { new MalformedResponseHandler(operation) };
        }

        public IReadOnlyList<IAgentOperationHandler> Handlers { get; }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class MalformedResponseHandler : IAgentOperationHandler
    {
        public MalformedResponseHandler(string operation)
        {
            OperationName = operation;
        }

        public string OperationName { get; }

        public Task<AgentHandlerResult> HandleAsync(
            AgentRequestContext context,
            JsonElement payload,
            CancellationToken cancellationToken)
        {
            var response = OperationName switch
            {
                "clr.describeObject" => "{\"object\":{\"handle\":null,\"boundaryId\":\"boundary\"}}",
                "clr.listMembers" => "{\"members\":[null],\"nextContinuationToken\":null}",
                "clr.readFieldValues" => "{\"results\":[null]}",
                "clr.readPropertyValue" => "{\"result\":{\"member\":null,\"outcome\":\"Available\",\"value\":{\"kind\":\"Null\"}}}",
                _ => throw new ArgumentOutOfRangeException()
            };
            using var document = JsonDocument.Parse(response);
            return Task.FromResult(AgentHandlerResult.Success(document.RootElement.Clone()));
        }
    }
}
