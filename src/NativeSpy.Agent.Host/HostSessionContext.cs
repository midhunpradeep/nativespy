using NativeSpy.Protocol.Common;

namespace NativeSpy.Agent.Host;

public sealed class HostSessionContext
{
    internal HostSessionContext(
        string sessionId,
        int protocolVersion,
        ProcessIdentityDto targetProcessIdentity)
    {
        SessionId = sessionId;
        ProtocolVersion = protocolVersion;
        TargetProcessIdentity = targetProcessIdentity;
    }

    public string SessionId { get; }

    public int ProtocolVersion { get; }

    public ProcessIdentityDto TargetProcessIdentity { get; }
}
