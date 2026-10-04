namespace NativeSpy.Protocol.Json;

public sealed class ProtocolJsonException : Exception
{
    public ProtocolJsonException(string message)
        : base(message)
    {
    }

    public ProtocolJsonException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
