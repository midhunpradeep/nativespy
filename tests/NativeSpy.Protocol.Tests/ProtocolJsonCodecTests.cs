using System.Text;
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
                new[] { "beginCurrentHwnd" }),
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
}
