using System.Text.Json;

namespace NativeSpy.Protocol.Json;

public static class ProtocolJsonValidationExtensions
{
    public static void EnsureMessageKind(this BootstrapDescriptorWire descriptor)
    {
        if (!string.Equals(descriptor.Kind, "nativespy.bootstrap", StringComparison.Ordinal)
            || descriptor.DescriptorVersion != 1
            || descriptor.MinSupportedVersion <= 0
            || descriptor.MaxSupportedVersion < descriptor.MinSupportedVersion)
        {
            throw new ProtocolJsonException("The bootstrap descriptor header is invalid.");
        }
    }

    public static void EnsureMessageKind(this HelloRequestWire request)
    {
        if (!string.Equals(request.MessageKind, "hello", StringComparison.Ordinal)
            || request.MinSupportedVersion <= 0
            || request.MaxSupportedVersion < request.MinSupportedVersion)
        {
            throw new ProtocolJsonException("The hello request header is invalid.");
        }
    }

    public static void EnsureMessageKind(this HelloResponseWire response)
    {
        if (!string.Equals(response.MessageKind, "helloResponse", StringComparison.Ordinal)
            || response.SelectedProtocolVersion <= 0)
        {
            throw new ProtocolJsonException("The hello response header is invalid.");
        }
    }

    public static void EnsureMessageKind(this RequestEnvelopeWire request)
    {
        if (!string.Equals(request.MessageKind, "request", StringComparison.Ordinal)
            || request.ProtocolVersion <= 0
            || string.IsNullOrWhiteSpace(request.SessionId)
            || string.IsNullOrWhiteSpace(request.RequestId)
            || string.IsNullOrWhiteSpace(request.Operation))
        {
            throw new ProtocolJsonException("The request envelope header is invalid.");
        }
    }

    public static void EnsureMessageKind(this ResponseEnvelopeWire response)
    {
        if (!string.Equals(response.MessageKind, "response", StringComparison.Ordinal)
            || response.ProtocolVersion <= 0
            || string.IsNullOrWhiteSpace(response.SessionId)
            || string.IsNullOrWhiteSpace(response.RequestId))
        {
            throw new ProtocolJsonException("The response envelope header is invalid.");
        }
    }
}
