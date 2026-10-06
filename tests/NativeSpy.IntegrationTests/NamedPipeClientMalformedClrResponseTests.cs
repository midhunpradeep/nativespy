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
    public Task Malformed_clr_success_payloads_become_terminal_protocol_failures(string operation)
    {
        return AssertMalformedResponseTerminalAsync(operation, response: null);
    }

    [Fact]
    public Task Describe_success_with_a_different_handle_is_terminal()
    {
        return AssertMalformedResponseTerminalAsync(
            "clr.describeObject",
            DescribeResponse(ManagedObjectJson(handleId: "other")));
    }

    [Fact]
    public Task Describe_success_with_an_incompatible_type_is_terminal()
    {
        return AssertMalformedResponseTerminalAsync(
            "clr.describeObject",
            DescribeResponse(ManagedObjectJson(typeId: "other-type")));
    }

    [Fact]
    public Task List_success_with_a_member_outside_the_request_scope_is_terminal()
    {
        return AssertMalformedResponseTerminalAsync(
            "clr.listMembers",
            ListResponse(MemberJson("member", sessionId: "other")));
    }

    [Fact]
    public Task List_success_with_a_member_outside_the_filter_is_terminal()
    {
        return AssertMalformedResponseTerminalAsync(
            "clr.listMembers",
            ListResponse(MemberJson("member", kind: "Property")),
            ClrMemberKindFilter.Fields);
    }

    [Fact]
    public Task List_success_that_exceeds_the_requested_page_is_terminal()
    {
        return AssertMalformedResponseTerminalAsync(
            "clr.listMembers",
            ListResponse(MemberJson("member-1") + "," + MemberJson("member-2")));
    }

    [Fact]
    public Task List_success_with_duplicate_member_identity_is_terminal()
    {
        return AssertMalformedResponseTerminalAsync(
            "clr.listMembers",
            ListResponse(MemberJson("member"), MemberJson("member")));
    }

    [Fact]
    public Task Field_success_with_the_wrong_result_count_is_terminal()
    {
        return AssertMalformedResponseTerminalAsync(
            "clr.readFieldValues",
            "{\"results\":[]}");
    }

    [Fact]
    public Task Field_success_with_a_different_member_is_terminal()
    {
        return AssertMalformedResponseTerminalAsync(
            "clr.readFieldValues",
            FieldUnavailableResponse("session", "InvalidMemberReference", memberId: "other"));
    }

    [Fact]
    public Task Property_success_with_a_different_member_is_terminal()
    {
        return AssertMalformedResponseTerminalAsync(
            "clr.readPropertyValue",
            PropertyResponse(MemberJsonReference("other"), "Unsupported"));
    }

    [Fact]
    public async Task Legitimate_invalid_field_reference_result_remains_member_local()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var options = CreateOptions();
        await using var host = new AgentHost(
            options,
            new MalformedResponseCompositionFactory(
                "clr.readFieldValues",
                FieldUnavailableResponse("other", "InvalidMemberReference")));
        await host.StartAsync();
        await using var client = await NamedPipeClientSession.ConnectAsync(
            host.BootstrapDescriptor,
            options.TargetProcessIdentity,
            cancellationToken: CancellationToken.None);

        var result = await client.ReadFieldValuesAsync(
            CreateObject(),
            new[] { new MemberRefDto("other", "member", "boundary", "type") },
            CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.Equal(ClrReadOutcome.Unavailable, result.Value!.Results[0].Outcome);
        Assert.Equal(OperationErrorCode.InvalidMemberReference, result.Value.Results[0].ErrorCode);
        Assert.Equal(AgentHostState.Active, host.State);
    }

    [Fact]
    public async Task Legitimate_unsupported_property_result_remains_a_success()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var options = CreateOptions();
        await using var host = new AgentHost(
            options,
            new MalformedResponseCompositionFactory(
                "clr.readPropertyValue",
                PropertyResponse(MemberJsonReference("member"), "Unsupported")));
        await host.StartAsync();
        await using var client = await NamedPipeClientSession.ConnectAsync(
            host.BootstrapDescriptor,
            options.TargetProcessIdentity,
            cancellationToken: CancellationToken.None);

        var first = await client.ReadPropertyValueAsync(
            CreateObject(),
            new MemberRefDto("session", "member", "boundary", "type"),
            CancellationToken.None);
        Assert.True(first.IsSuccess);
        Assert.Equal(ClrReadOutcome.Unsupported, first.Value!.Result.Outcome);

        var second = await client.ReadPropertyValueAsync(
            CreateObject(),
            new MemberRefDto("session", "member", "boundary", "type"),
            CancellationToken.None);
        Assert.True(second.IsSuccess);
        Assert.Equal(AgentHostState.Active, host.State);
    }

    [Fact]
    public async Task Legitimate_top_level_operation_failure_remains_typed_and_nonterminal()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var options = CreateOptions();
        await using var host = new AgentHost(
            options,
            new OperationErrorCompositionFactory(
                "clr.readPropertyValue",
                new OperationErrorDto(OperationErrorCode.TargetTimeout, "getter timed out")));
        await host.StartAsync();
        await using var client = await NamedPipeClientSession.ConnectAsync(
            host.BootstrapDescriptor,
            options.TargetProcessIdentity,
            cancellationToken: CancellationToken.None);

        var result = await client.ReadPropertyValueAsync(
            CreateObject(),
            new MemberRefDto("session", "member", "boundary", "type"),
            CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal(OperationErrorCode.TargetTimeout, result.Error!.Code);

        var retry = await client.ReadPropertyValueAsync(
            CreateObject(),
            new MemberRefDto("session", "member", "boundary", "type"),
            CancellationToken.None);
        Assert.False(retry.IsSuccess);
        Assert.Equal(OperationErrorCode.TargetTimeout, retry.Error!.Code);
        Assert.Equal(AgentHostState.Active, host.State);
    }

    [Theory]
    [InlineData(ProtocolJsonCodec.OperationErrorStatus)]
    [InlineData(ProtocolJsonCodec.ProtocolErrorStatus)]
    public async Task Unknown_response_error_codes_are_terminal_protocol_failures(string status)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var identity = ProcessIdentityReader.ReadCurrent();
        var pipeName = "nativespy-unknown-code-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(8));
        await using var server = new NamedPipeServer(
            new NamedPipeServerOptions(pipeName, NamedPipeSecurity.GetCurrentUserSid()));
        server.Bind();
        var serverTask = ServeUnknownCodeAsync(server, identity, status);
        var bootstrap = new BootstrapDescriptorWire
        {
            PipeName = pipeName,
            BootstrapNonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16)),
            TargetProcessIdentity = new ProcessIdentityWire
            {
                ProcessId = identity.ProcessId,
                ProcessStartIdentity = identity.ProcessStartIdentity
            },
            MinSupportedVersion = 1,
            MaxSupportedVersion = 1
        };
        await using var client = await NamedPipeClientSession.ConnectAsync(
            bootstrap,
            identity,
            cancellationToken: CancellationToken.None);

        var exception = await Assert.ThrowsAsync<NamedPipeProtocolException>(() =>
            client.ReadPropertyValueAsync(
                CreateObject(),
                new MemberRefDto("session", "member", "boundary", "type"),
                CancellationToken.None));
        Assert.Equal(ProtocolErrorCode.ProtocolViolation, exception.Error.Code);
        await serverTask;

        await Assert.ThrowsAsync<NamedPipeProtocolException>(() =>
            client.ReadPropertyValueAsync(
                CreateObject(),
                new MemberRefDto("session", "member", "boundary", "type"),
                CancellationToken.None));
    }

    [Fact]
    public Task Missing_type_identity_is_value_type_uses_terminal_protocol_failure_policy()
    {
        var missingIsValueType = ManagedObjectJson()
            .Replace(",\"isValueType\":false", string.Empty, StringComparison.Ordinal);
        return AssertMalformedResponseTerminalAsync(
            "clr.describeObject",
            DescribeResponse(missingIsValueType));
    }

    [Fact]
    public Task Missing_target_exception_wrapper_flag_uses_terminal_protocol_failure_policy()
    {
        return AssertMalformedResponseTerminalAsync(
            "clr.readPropertyValue",
            TargetFailedPropertyResponse(includeWrapper: false));
    }

    [Fact]
    public async Task Missing_type_identity_collection_uses_terminal_protocol_failure_policy()
    {
        await AssertMalformedResponseTerminalAsync(
            "clr.describeObject",
            "{\"object\":{\"handle\":{\"sessionId\":\"session\",\"handleId\":\"object\",\"generation\":1,\"kind\":\"ClrObject\",\"boundaryId\":\"boundary\"},\"typeIdentity\":{\"typeId\":\"type\",\"fullName\":\"Example.Type\",\"assemblySimpleName\":\"Example\",\"boundaryId\":\"boundary\",\"isValueType\":false,\"interfaces\":[]},\"boundaryId\":\"boundary\"}}");
    }

    [Fact]
    public async Task Missing_value_type_struct_fields_uses_terminal_protocol_failure_policy()
    {
        await AssertMalformedResponseTerminalAsync(
            "clr.readFieldValues",
            "{\"results\":[{\"member\":{\"sessionId\":\"session\",\"memberId\":\"member\",\"boundaryId\":\"boundary\",\"declaringTypeId\":\"type\"},\"outcome\":\"Available\",\"value\":{\"kind\":\"ValueType\",\"valueType\":{\"typeId\":\"value\",\"fullName\":\"Example.Value\",\"assemblySimpleName\":\"Example\",\"boundaryId\":\"boundary\",\"isValueType\":true,\"genericArguments\":[],\"interfaces\":[]},\"structTruncated\":false,\"structNotExpanded\":true}}]}");
    }

    private static async Task AssertMalformedResponseTerminalAsync(
        string operation,
        string? response,
        ClrMemberKindFilter filter = ClrMemberKindFilter.All)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var options = CreateOptions();
        await using var host = new AgentHost(
            options,
            new MalformedResponseCompositionFactory(operation, response));
        await host.StartAsync();
        await using var client = await NamedPipeClientSession.ConnectAsync(
            host.BootstrapDescriptor,
            options.TargetProcessIdentity,
            cancellationToken: CancellationToken.None);

        var exception = await Assert.ThrowsAsync<NamedPipeProtocolException>(() => InvokeAsync(client, operation, filter));

        Assert.Equal(ProtocolErrorCode.ProtocolViolation, exception.Error.Code);
        Assert.Contains("invalid CLR success payload", exception.Message, StringComparison.OrdinalIgnoreCase);
        await Assert.ThrowsAsync<NamedPipeProtocolException>(() => InvokeAsync(client, operation, filter));
    }

    private static async Task InvokeAsync(
        NamedPipeClientSession client,
        string operation,
        ClrMemberKindFilter filter = ClrMemberKindFilter.All)
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
                    filter,
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

    private static async Task ServeUnknownCodeAsync(
        NamedPipeServer server,
        ProcessIdentityDto identity,
        string status)
    {
        await using var connection = await server.AcceptAsync();
        using var helloFrame = await connection.ReadFrameAsync()
            ?? throw new InvalidOperationException("The client closed during the test handshake.");
        var hello = ProtocolJsonCodec.DeserializeHelloRequest(helloFrame.Payload);
        hello.EnsureMessageKind();
        var sessionId = "unknown-code-session";
        await connection.WriteFrameAsync(
            ProtocolJsonCodec.SerializeHelloResponse(new HelloResponseWire
            {
                SelectedProtocolVersion = 1,
                SessionId = sessionId,
                TargetProcessIdentity = new ProcessIdentityWire
                {
                    ProcessId = identity.ProcessId,
                    ProcessStartIdentity = identity.ProcessStartIdentity
                },
                Capabilities = new CapabilitiesWire
                {
                    SupportedOperations = new[] { "clr.readPropertyValue" },
                    SingleClient = true,
                    ReconnectSupported = false
                },
                Limits = new LimitsWire
                {
                    MaxFrameBytes = ProtocolWireConstants.DefaultMaximumFrameBytes,
                    MaxJsonDepth = ProtocolWireConstants.DefaultMaximumJsonDepth,
                    DefaultBudgetMs = 1000,
                    MaxBudgetMs = 1000,
                    MaxOutstandingRequests = 1
                }
            }));

        using var requestFrame = await connection.ReadFrameAsync()
            ?? throw new InvalidOperationException("The client closed before the test request.");
        var request = ProtocolJsonCodec.DeserializeRequest(requestFrame.Payload);
        await connection.WriteFrameAsync(
            ProtocolJsonCodec.SerializeResponse(new ResponseEnvelopeWire
            {
                ProtocolVersion = 1,
                SessionId = sessionId,
                RequestId = request.RequestId,
                ResultStatus = status,
                OperationError = status == ProtocolJsonCodec.OperationErrorStatus
                    ? new OperationErrorWire { Code = "UnknownOperationError" }
                    : null,
                ProtocolError = status == ProtocolJsonCodec.ProtocolErrorStatus
                    ? new ProtocolErrorWire { Code = "UnknownProtocolError" }
                    : null
            }));

        // Keep the peer alive long enough for the client to classify the
        // response rather than racing the terminal EOF with response decode.
        await Task.Delay(250);
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

    private static string DescribeResponse(string managedObject)
    {
        return "{\"object\":" + managedObject + "}";
    }

    private static string ManagedObjectJson(
        string handleId = "object",
        string typeId = "type")
    {
        return "{\"handle\":{\"sessionId\":\"session\",\"handleId\":\""
            + handleId
            + "\",\"generation\":1,\"kind\":\"ClrObject\",\"boundaryId\":\"boundary\"},"
            + "\"typeIdentity\":{\"typeId\":\"" + typeId
            + "\",\"fullName\":\"Example.Type\",\"assemblySimpleName\":\"Example\",\"boundaryId\":\"boundary\",\"isValueType\":false,\"genericArguments\":[],\"interfaces\":[]},"
            + "\"boundaryId\":\"boundary\"}";
    }

    private static string MemberJson(
        string memberId,
        string sessionId = "session",
        string kind = "Field")
    {
        return "{\"member\":" + MemberJsonReference(memberId, sessionId)
            + ",\"kind\":\"" + kind + "\",\"name\":\"" + kind + "-" + memberId
            + "\",\"valueType\":{\"typeId\":\"int\",\"boundaryId\":\"boundary\"}}";
    }

    private static string MemberJsonReference(
        string memberId,
        string sessionId = "session")
    {
        return "{\"sessionId\":\"" + sessionId + "\",\"memberId\":\"" + memberId
            + "\",\"boundaryId\":\"boundary\",\"declaringTypeId\":\"type\"}";
    }

    private static string ListResponse(params string[] members)
    {
        return "{\"members\":[" + string.Join(",", members) + "],\"nextContinuationToken\":null}";
    }

    private static string PropertyResponse(string member, string outcome)
    {
        return "{\"result\":{\"member\":" + member + ",\"outcome\":\"" + outcome + "\"}}";
    }

    private static string TargetFailedPropertyResponse(bool includeWrapper)
    {
        var wrapper = includeWrapper ? ",\"wasReflectionWrapper\":false" : string.Empty;
        return "{\"result\":{\"member\":" + MemberJsonReference("member")
            + ",\"outcome\":\"TargetFailed\",\"targetException\":{\"exceptionType\":{\"typeId\":\"exception\",\"fullName\":\"Example.Exception\",\"assemblySimpleName\":\"Example\",\"boundaryId\":\"boundary\",\"isValueType\":false,\"genericArguments\":[],\"interfaces\":[]}"
            + wrapper
            + "}}}";
    }

    private static string FieldUnavailableResponse(
        string sessionId,
        string errorCode,
        string memberId = "member")
    {
        return "{\"results\":[{\"member\":{\"sessionId\":\"" + sessionId
            + "\",\"memberId\":\"" + memberId
            + "\",\"boundaryId\":\"boundary\",\"declaringTypeId\":\"type\"},"
            + "\"outcome\":\"Unavailable\",\"errorCode\":\"" + errorCode + "\"}]}";
    }

    private sealed class MalformedResponseCompositionFactory : IAgentHostCompositionFactory
    {
        private readonly string _operation;
        private readonly string? _response;

        public MalformedResponseCompositionFactory(string operation, string? response = null)
        {
            _operation = operation;
            _response = response;
        }

        public IReadOnlyList<string> DeclaredOperationNames => new[] { _operation };

        public IAgentHostComposition Create(
            IManagedObjectReferenceService identityService,
            HostSessionContext context)
        {
            return new MalformedResponseComposition(_operation, _response, operationError: null);
        }
    }

    private sealed class OperationErrorCompositionFactory : IAgentHostCompositionFactory
    {
        private readonly string _operation;
        private readonly OperationErrorDto _error;

        public OperationErrorCompositionFactory(string operation, OperationErrorDto error)
        {
            _operation = operation;
            _error = error;
        }

        public IReadOnlyList<string> DeclaredOperationNames => new[] { _operation };

        public IAgentHostComposition Create(
            IManagedObjectReferenceService identityService,
            HostSessionContext context)
        {
            return new MalformedResponseComposition(_operation, response: null, _error);
        }
    }

    private sealed class MalformedResponseComposition : IAgentHostComposition
    {
        public MalformedResponseComposition(
            string operation,
            string? response,
            OperationErrorDto? operationError)
        {
            Handlers = new[] { new MalformedResponseHandler(operation, response, operationError) };
        }

        public IReadOnlyList<IAgentOperationHandler> Handlers { get; }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class MalformedResponseHandler : IAgentOperationHandler
    {
        private readonly string? _response;
        private readonly OperationErrorDto? _operationError;

        public MalformedResponseHandler(
            string operation,
            string? response,
            OperationErrorDto? operationError)
        {
            OperationName = operation;
            _response = response;
            _operationError = operationError;
        }

        public string OperationName { get; }

        public Task<AgentHandlerResult> HandleAsync(
            AgentRequestContext context,
            JsonElement payload,
            CancellationToken cancellationToken)
        {
            if (_operationError is not null)
            {
                return Task.FromResult(AgentHandlerResult.Error(_operationError));
            }

            var response = _response ?? OperationName switch
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
