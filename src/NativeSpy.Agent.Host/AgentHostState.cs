namespace NativeSpy.Agent.Host;

public enum AgentHostState
{
    Created,
    Listening,
    Authenticating,
    Activating,
    Active,
    Closing,
    Closed
}
