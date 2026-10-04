using System.Text;
using System.Text.Json;
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

    private static string RequestJson(string payload)
    {
        return "{\"messageKind\":\"request\",\"protocolVersion\":1,\"sessionId\":\"s\",\"requestId\":\"1\",\"operation\":\"test.echo\",\"payload\":"
            + payload
            + "}";
    }
}
