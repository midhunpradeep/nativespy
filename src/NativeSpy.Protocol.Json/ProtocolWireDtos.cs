using System.Text.Json;
using NativeSpy.Protocol.Common;

namespace NativeSpy.Protocol.Json;

public static class ProtocolWireConstants
{
    public const int CurrentProtocolVersion = 1;
    public const int DefaultMaximumFrameBytes = 1024 * 1024;
    public const int AbsoluteMaximumFrameBytes = 4 * 1024 * 1024;
    public const int DefaultMaximumJsonDepth = 64;
    public const int MaximumBootstrapBytes = 16 * 1024;
}

public sealed class BootstrapDescriptorWire
{
    public string Kind { get; set; } = "nativespy.bootstrap";
    public int DescriptorVersion { get; set; } = 1;
    public string PipeName { get; set; } = string.Empty;
    public string BootstrapNonce { get; set; } = string.Empty;
    public ProcessIdentityWire TargetProcessIdentity { get; set; } = new();
    public int MinSupportedVersion { get; set; }
    public int MaxSupportedVersion { get; set; }
}

public sealed class HelloRequestWire
{
    public string MessageKind { get; set; } = "hello";
    public int MinSupportedVersion { get; set; }
    public int MaxSupportedVersion { get; set; }
    public ProcessIdentityWire ExpectedTargetProcessIdentity { get; set; } = new();
    public string BootstrapNonce { get; set; } = string.Empty;
    public string? DiagnosticClientIdentity { get; set; }
}

public sealed class HelloResponseWire
{
    public string MessageKind { get; set; } = "helloResponse";
    public int SelectedProtocolVersion { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public ProcessIdentityWire TargetProcessIdentity { get; set; } = new();
    public CapabilitiesWire Capabilities { get; set; } = new();
    public LimitsWire Limits { get; set; } = new();
}

public sealed class HandshakeErrorWire
{
    public string MessageKind { get; set; } = "error";
    public ProtocolErrorWire Error { get; set; } = new();
}

public sealed class RequestEnvelopeWire
{
    public string MessageKind { get; set; } = "request";
    public int ProtocolVersion { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public string RequestId { get; set; } = string.Empty;
    public string Operation { get; set; } = string.Empty;
    public ulong? BudgetMs { get; set; }
    public JsonElement Payload { get; set; }
}

public sealed class ResponseEnvelopeWire
{
    public string MessageKind { get; set; } = "response";
    public int ProtocolVersion { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public string RequestId { get; set; } = string.Empty;
    public string ResultStatus { get; set; } = string.Empty;
    public JsonElement? Payload { get; set; }
    public ProtocolErrorWire? ProtocolError { get; set; }
    public OperationErrorWire? OperationError { get; set; }
}

public sealed class ProcessIdentityWire
{
    public int ProcessId { get; set; }
    public string ProcessStartIdentity { get; set; } = string.Empty;
}

public sealed class CapabilitiesWire
{
    public string[] SupportedOperations { get; set; } = Array.Empty<string>();
    public string[] AdapterIds { get; set; } = Array.Empty<string>();
    public bool SingleClient { get; set; }
    public bool ReconnectSupported { get; set; }
}

public sealed class LimitsWire
{
    public int MaxFrameBytes { get; set; }
    public int MaxJsonDepth { get; set; }
    public ulong DefaultBudgetMs { get; set; }
    public ulong MaxBudgetMs { get; set; }
    public int MaxOutstandingRequests { get; set; }
}

public sealed class ProtocolErrorWire
{
    public string Code { get; set; } = string.Empty;
    public string? Message { get; set; }
    public string? DiagnosticId { get; set; }
}

public sealed class OperationErrorWire
{
    public string Code { get; set; } = string.Empty;
    public string? Message { get; set; }
}

public sealed class BeginCurrentHwndPayloadWire
{
    public ulong Hwnd { get; set; }
}

public sealed class RevalidateCurrentHwndPayloadWire
{
    public ulong Hwnd { get; set; }
    public HandlePayloadWire CandidateHandle { get; set; } = new();
}

public sealed class HandlePayloadWire
{
    public string SessionId { get; set; } = string.Empty;
    public string HandleId { get; set; } = string.Empty;
    public long Generation { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string? BoundaryId { get; set; }
}
