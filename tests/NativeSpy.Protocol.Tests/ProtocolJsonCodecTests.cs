using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using NativeSpy.Protocol.Common;
using NativeSpy.Protocol.Correlation;
using NativeSpy.Protocol.Json;
using Xunit;

namespace NativeSpy.Protocol.Tests;

public sealed class ProtocolJsonCodecTests
{
    [Fact]
    public void Bootstrap_round_trips_with_canonical_camel_case_properties()
    {
        var descriptor = new BootstrapDescriptorWire
        {
            PipeName = "nativespy-test",
            BootstrapNonce = "nonce",
            TargetProcessIdentity = new ProcessIdentityWire
            {
                ProcessId = 42,
                ProcessStartIdentity = "123"
            },
            MinSupportedVersion = 1,
            MaxSupportedVersion = 1
        };

        var json = Encoding.UTF8.GetString(ProtocolJsonCodec.SerializeBootstrap(descriptor));
        Assert.Contains("\"targetProcessIdentity\"", json);
        Assert.DoesNotContain("TargetProcessIdentity", json);

        var parsed = ProtocolJsonCodec.DeserializeBootstrap(Encoding.UTF8.GetBytes(json));
        parsed.EnsureMessageKind();
        Assert.Equal("nativespy-test", parsed.PipeName);
        Assert.Equal("123", parsed.TargetProcessIdentity.ProcessStartIdentity);
    }

    [Fact]
    public void Duplicate_and_unknown_properties_are_rejected()
    {
        var duplicate = "{\"kind\":\"nativespy.bootstrap\",\"kind\":\"nativespy.bootstrap\",\"descriptorVersion\":1,\"pipeName\":\"p\",\"bootstrapNonce\":\"n\",\"targetProcessIdentity\":{\"processId\":1,\"processStartIdentity\":\"s\"},\"minSupportedVersion\":1,\"maxSupportedVersion\":1}";
        var unknown = "{\"kind\":\"nativespy.bootstrap\",\"descriptorVersion\":1,\"pipeName\":\"p\",\"bootstrapNonce\":\"n\",\"targetProcessIdentity\":{\"processId\":1,\"processStartIdentity\":\"s\"},\"minSupportedVersion\":1,\"maxSupportedVersion\":1,\"extra\":true}";

        Assert.Throws<ProtocolJsonException>(() => ProtocolJsonCodec.DeserializeBootstrap(Encoding.UTF8.GetBytes(duplicate)));
        Assert.Throws<ProtocolJsonException>(() => ProtocolJsonCodec.DeserializeBootstrap(Encoding.UTF8.GetBytes(unknown)));
    }

    [Fact]
    public void Successful_response_preserves_embedded_operation_error_as_framework_evidence()
    {
        var evidence = new FrameworkCorrelationEvidenceDto(
            "winforms",
            42,
            candidateTarget: null,
            new[]
            {
                new CorrelationEvidenceFactDto(
                    "ControlFromHandle",
                    ProofOutcome.NotAvailable,
                    EvidenceKind.Deterministic,
                    "not available")
            },
            new[]
            {
                new CorrelationValidationFactDto(
                    "CurrentHwndMatches",
                    ValidationOutcome.NotAvailable,
                    "not available")
            },
            new CorrelationEffectSummaryDto(
                new[] { EffectCategory.Passive },
                FrameworkStateEffect.None,
                ApplicationCallbackEffect.None,
                Array.Empty<CallbackDetailDto>(),
                VisibleMutationEffect.NotRequested,
                new[] { ProtocolOperationNames.BeginCurrentHwnd }),
            Array.Empty<AdapterMetadataDto>(),
            Array.Empty<CorrelationLimitationDto>(),
            new OperationErrorDto(OperationErrorCode.TargetTimeout, "expired"));
        var response = new ResponseEnvelopeWire
        {
            ProtocolVersion = 1,
            SessionId = "session-1",
            RequestId = "1",
            ResultStatus = ProtocolJsonCodec.SuccessStatus,
            Payload = ProtocolJsonCodec.SerializeFrameworkEvidence(evidence)
        };

        var parsed = ProtocolJsonCodec.DeserializeResponse(ProtocolJsonCodec.SerializeResponse(response));
        var roundTripped = ProtocolJsonCodec.DeserializeFrameworkEvidence(parsed.Payload!.Value);

        Assert.Equal(ProtocolJsonCodec.SuccessStatus, parsed.ResultStatus);
        Assert.Equal(OperationErrorCode.TargetTimeout, roundTripped.OperationError?.Code);
    }

    [Fact]
    public void Callback_count_known_is_intentionally_omission_safe()
    {
        var evidence = CreateFrameworkEvidence(
            callbackDetails: new[] { new CallbackDetailDto("Paint") });
        var json = JsonNode.Parse(
            ProtocolJsonCodec.SerializeFrameworkEvidence(evidence).GetRawText())!.AsObject();
        json["effects"]!["callbackDetails"]![0]!.AsObject().Remove("countKnown");

        var decoded = ProtocolJsonCodec.DeserializeFrameworkEvidence(
            JsonDocument.Parse(json.ToJsonString()).RootElement);
        var callback = Assert.Single(decoded.Effects.CallbackDetails);
        Assert.False(callback.CountKnown);
        Assert.Null(callback.Count);
    }

    [Fact]
    public void Required_framework_numeric_fields_reject_defaulting_omission()
    {
        var processId = JsonNode.Parse(
            ProtocolJsonCodec.SerializeFrameworkEvidence(CreateFrameworkEvidence()).GetRawText())!.AsObject();
        processId.Remove("processId");
        Assert.Throws<ProtocolJsonException>(() =>
            ProtocolJsonCodec.DeserializeFrameworkEvidence(
                JsonDocument.Parse(processId.ToJsonString()).RootElement));

        var metadata = JsonNode.Parse(
            ProtocolJsonCodec.SerializeFrameworkEvidence(
                CreateFrameworkEvidence(
                    adapterMetadata: new[]
                    {
                        new AdapterMetadataDto(
                            "adapter",
                            "schema",
                            1,
                            DetachedMetadataValueDto.Null())
                    })).GetRawText())!.AsObject();
        metadata["adapterMetadata"]![0]!.AsObject().Remove("schemaVersion");
        Assert.Throws<ProtocolJsonException>(() =>
            ProtocolJsonCodec.DeserializeFrameworkEvidence(
                JsonDocument.Parse(metadata.ToJsonString()).RootElement));

        var handle = JsonNode.Parse(
            ProtocolJsonCodec.SerializeFrameworkEvidence(
                CreateFrameworkEvidence(
                    candidateTarget: new CorrelationTargetRefDto(
                        CorrelationTargetKind.FrameworkEntity,
                        framework: new FrameworkEntityRefDto(
                            "adapter",
                            FrameworkEntityKind.GridCoordinate,
                            liveHandle: new HandleRefDto(
                                "session",
                                "entity",
                                1,
                                HandleKind.AgentEntity,
                                "boundary"))))).GetRawText())!.AsObject();
        handle["candidateTarget"]!["framework"]!["liveHandle"]!.AsObject().Remove("generation");
        Assert.Throws<ProtocolJsonException>(() =>
            ProtocolJsonCodec.DeserializeFrameworkEvidence(
                JsonDocument.Parse(handle.ToJsonString()).RootElement));

        var hwnd = JsonNode.Parse(
            ProtocolJsonCodec.SerializeFrameworkEvidence(
                CreateFrameworkEvidence(
                    candidateTarget: new CorrelationTargetRefDto(
                        CorrelationTargetKind.NativeEntity,
                        native: new NativeEntityRefDto(
                            NativeBoundaryKind.HwndShell,
                            new HwndInfoDto(123, "hwnd-1"),
                            processId: 42)))).GetRawText())!.AsObject();
        hwnd["candidateTarget"]!["native"]!["hwndObservation"]!.AsObject().Remove("hwnd");
        Assert.Throws<ProtocolJsonException>(() =>
            ProtocolJsonCodec.DeserializeFrameworkEvidence(
                JsonDocument.Parse(hwnd.ToJsonString()).RootElement));
    }

    [Fact]
    public void Generic_request_payload_preserves_primitives_and_arrays()
    {
        var primitive = ProtocolJsonCodec.DeserializeRequest(Encoding.UTF8.GetBytes(
            RequestJson("42")));
        var array = ProtocolJsonCodec.DeserializeRequest(Encoding.UTF8.GetBytes(
            RequestJson("[1,2,3]")));

        Assert.Equal(JsonValueKind.Number, primitive.Payload.ValueKind);
        Assert.Equal(42, primitive.Payload.GetInt32());
        Assert.Equal(JsonValueKind.Array, array.Payload.ValueKind);
        Assert.Equal(3, array.Payload.GetArrayLength());
        Assert.Throws<ProtocolJsonException>(
            () => ProtocolJsonCodec.ReadBeginCurrentHwndPayload(primitive.Payload));
    }

    [Fact]
    public void Required_payload_missing_and_null_are_rejected()
    {
        var missing = "{\"messageKind\":\"request\",\"protocolVersion\":1,\"sessionId\":\"s\",\"requestId\":\"1\",\"operation\":\"test.echo\"}";
        var nullPayload = missing[..^1] + ",\"payload\":null}";

        Assert.Throws<ProtocolJsonException>(() =>
            ProtocolJsonCodec.DeserializeRequest(Encoding.UTF8.GetBytes(missing)));
        Assert.Throws<ProtocolJsonException>(() =>
            ProtocolJsonCodec.DeserializeRequest(Encoding.UTF8.GetBytes(nullPayload)));
    }

    [Fact]
    public void Invalid_utf8_trailing_json_and_numeric_enums_are_rejected()
    {
        Assert.Throws<ProtocolJsonException>(() =>
            ProtocolJsonCodec.DeserializeBootstrap(new byte[] { (byte)'{', 0xff }));
        Assert.Throws<ProtocolJsonException>(() =>
            ProtocolJsonCodec.DeserializeBootstrap(Encoding.UTF8.GetBytes(
                "{\"kind\":\"nativespy.bootstrap\"} {}")));

        var numericKind = JsonDocument.Parse(
            "{\"hwnd\":1,\"candidateHandle\":{\"sessionId\":\"s\",\"handleId\":\"h\",\"generation\":1,\"kind\":0}}")
            .RootElement.Clone();
        Assert.Throws<ProtocolJsonException>(() =>
            ProtocolJsonCodec.ReadRevalidateCurrentHwndPayload(numericKind));
    }

    [Fact]
    public void Json_depth_is_bounded_and_response_branches_are_exclusive()
    {
        var belowLimit = ProtocolJsonCodec.DeserializeRequest(Encoding.UTF8.GetBytes(
            RequestJson("[[[0]]]")));
        Assert.Equal(JsonValueKind.Array, belowLimit.Payload.ValueKind);

        var aboveLimit = RequestJson(new string('[', ProtocolWireConstants.DefaultMaximumJsonDepth + 2)
            + "0"
            + new string(']', ProtocolWireConstants.DefaultMaximumJsonDepth + 2));
        Assert.Throws<ProtocolJsonException>(() =>
            ProtocolJsonCodec.DeserializeRequest(Encoding.UTF8.GetBytes(aboveLimit)));

        var invalidResponse = new ResponseEnvelopeWire
        {
            ProtocolVersion = 1,
            SessionId = "s",
            RequestId = "1",
            ResultStatus = ProtocolJsonCodec.SuccessStatus,
            Payload = JsonDocument.Parse("{}").RootElement.Clone(),
            ProtocolError = new ProtocolErrorWire { Code = nameof(ProtocolErrorCode.InvalidRequest) }
        };
        Assert.Throws<ProtocolJsonException>(() => ProtocolJsonCodec.SerializeResponse(invalidResponse));
    }

    [Fact]
    public void Hello_capability_booleans_are_required_and_explicit_false_round_trips()
    {
        var response = new HelloResponseWire
        {
            SelectedProtocolVersion = 1,
            SessionId = "session",
            TargetProcessIdentity = new ProcessIdentityWire
            {
                ProcessId = 42,
                ProcessStartIdentity = "100"
            },
            Capabilities = new CapabilitiesWire
            {
                SupportedOperations = Array.Empty<string>(),
                AdapterIds = Array.Empty<string>(),
                SingleClient = false,
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
        };

        var json = JsonNode.Parse(
            Encoding.UTF8.GetString(ProtocolJsonCodec.SerializeHelloResponse(response)))!.AsObject();
        Assert.False(json["capabilities"]!["singleClient"]!.GetValue<bool>());
        Assert.False(json["capabilities"]!["reconnectSupported"]!.GetValue<bool>());
        var parsed = ProtocolJsonCodec.DeserializeHelloResponse(
            Encoding.UTF8.GetBytes(json.ToJsonString()));
        Assert.False(parsed.Capabilities.SingleClient);
        Assert.False(parsed.Capabilities.ReconnectSupported);

        var missingSingleClient = JsonNode.Parse(json.ToJsonString())!.AsObject();
        missingSingleClient["capabilities"]!.AsObject().Remove("singleClient");
        Assert.Throws<ProtocolJsonException>(() =>
            ProtocolJsonCodec.DeserializeHelloResponse(
                Encoding.UTF8.GetBytes(missingSingleClient.ToJsonString())));

        var missingReconnectSupported = JsonNode.Parse(json.ToJsonString())!.AsObject();
        missingReconnectSupported["capabilities"]!.AsObject().Remove("reconnectSupported");
        Assert.Throws<ProtocolJsonException>(() =>
            ProtocolJsonCodec.DeserializeHelloResponse(
                Encoding.UTF8.GetBytes(missingReconnectSupported.ToJsonString())));
    }

    [Fact]
    public void Required_protocol_numeric_fields_do_not_default_from_omission()
    {
        var bootstrap = JsonNode.Parse(
            "{\"kind\":\"nativespy.bootstrap\",\"descriptorVersion\":1,\"pipeName\":\"p\",\"bootstrapNonce\":\"n\",\"targetProcessIdentity\":{\"processId\":1,\"processStartIdentity\":\"s\"},\"minSupportedVersion\":1,\"maxSupportedVersion\":1}")!.AsObject();
        bootstrap.Remove("descriptorVersion");
        Assert.Throws<ProtocolJsonException>(() =>
            ProtocolJsonCodec.DeserializeBootstrap(Encoding.UTF8.GetBytes(bootstrap.ToJsonString())));

        var request = JsonNode.Parse(RequestJson("{}"))!.AsObject();
        request.Remove("protocolVersion");
        Assert.Throws<ProtocolJsonException>(() =>
            ProtocolJsonCodec.DeserializeRequest(Encoding.UTF8.GetBytes(request.ToJsonString())));

        var response = JsonNode.Parse(
            "{\"messageKind\":\"response\",\"protocolVersion\":1,\"sessionId\":\"s\",\"requestId\":\"1\",\"resultStatus\":\"success\",\"payload\":{}}")!.AsObject();
        response.Remove("protocolVersion");
        Assert.Throws<ProtocolJsonException>(() =>
            ProtocolJsonCodec.DeserializeResponse(Encoding.UTF8.GetBytes(response.ToJsonString())));

        Assert.Throws<ProtocolJsonException>(() =>
            ProtocolJsonCodec.ReadBeginCurrentHwndPayload(JsonDocument.Parse("{}").RootElement));

        var revalidate = JsonNode.Parse(
            "{\"hwnd\":1,\"candidateHandle\":{\"sessionId\":\"s\",\"handleId\":\"h\",\"generation\":1,\"kind\":\"AgentEntity\"}}")!.AsObject();
        revalidate["candidateHandle"]!.AsObject().Remove("generation");
        Assert.Throws<ProtocolJsonException>(() =>
            ProtocolJsonCodec.ReadRevalidateCurrentHwndPayload(
                JsonDocument.Parse(revalidate.ToJsonString()).RootElement));
    }

    [Fact]
    public void Hello_and_response_round_trip_with_string_enum_errors()
    {
        var hello = new HelloRequestWire
        {
            MinSupportedVersion = 1,
            MaxSupportedVersion = 1,
            ExpectedTargetProcessIdentity = new ProcessIdentityWire
            {
                ProcessId = 42,
                ProcessStartIdentity = "100"
            },
            BootstrapNonce = "nonce"
        };
        var parsedHello = ProtocolJsonCodec.DeserializeHelloRequest(
            ProtocolJsonCodec.SerializeHelloRequest(hello));
        parsedHello.EnsureMessageKind();

        var response = new ResponseEnvelopeWire
        {
            ProtocolVersion = 1,
            SessionId = "s",
            RequestId = "1",
            ResultStatus = ProtocolJsonCodec.ProtocolErrorStatus,
            ProtocolError = new ProtocolErrorWire
            {
                Code = nameof(ProtocolErrorCode.ProtocolMismatch)
            }
        };
        var parsedResponse = ProtocolJsonCodec.DeserializeResponse(
            ProtocolJsonCodec.SerializeResponse(response));

        Assert.Equal(ProtocolJsonCodec.ProtocolErrorStatus, parsedResponse.ResultStatus);
        Assert.Equal(nameof(ProtocolErrorCode.ProtocolMismatch), parsedResponse.ProtocolError?.Code);
    }

    private static FrameworkCorrelationEvidenceDto CreateFrameworkEvidence(
        int processId = 42,
        CorrelationTargetRefDto? candidateTarget = null,
        IEnumerable<AdapterMetadataDto>? adapterMetadata = null,
        IEnumerable<CallbackDetailDto>? callbackDetails = null)
    {
        return new FrameworkCorrelationEvidenceDto(
            "winforms",
            processId,
            candidateTarget,
            new[]
            {
                new CorrelationEvidenceFactDto(
                    "ControlFromHandle",
                    ProofOutcome.Passed,
                    EvidenceKind.Deterministic)
            },
            new[]
            {
                new CorrelationValidationFactDto(
                    "CurrentHwndMatches",
                    ValidationOutcome.Passed)
            },
            new CorrelationEffectSummaryDto(
                new[] { EffectCategory.Passive },
                FrameworkStateEffect.None,
                ApplicationCallbackEffect.None,
                callbackDetails ?? Array.Empty<CallbackDetailDto>(),
                VisibleMutationEffect.NotRequested,
                new[] { ProtocolOperationNames.BeginCurrentHwnd }),
            adapterMetadata ?? Array.Empty<AdapterMetadataDto>(),
            Array.Empty<CorrelationLimitationDto>());
    }

    private static string RequestJson(string payload)
    {
        return "{\"messageKind\":\"request\",\"protocolVersion\":1,\"sessionId\":\"s\",\"requestId\":\"1\",\"operation\":\"test.echo\",\"payload\":"
            + payload
            + "}";
    }
}
