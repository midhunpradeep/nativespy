namespace NativeSpy.Protocol.Common;

/// <summary>
/// Detached identity for one target process instance. The start identity is opaque
/// and must only be compared for exact equality by protocol consumers.
/// </summary>
public sealed class ProcessIdentityDto
{
    public ProcessIdentityDto(int processId, string processStartIdentity)
    {
        if (processId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(processId), processId, "ProcessId must be positive.");
        }

        ProcessId = processId;
        ProcessStartIdentity = ContractValidation.RequiredIdentifier(
            processStartIdentity,
            nameof(processStartIdentity));
    }

    public int ProcessId { get; }

    public string ProcessStartIdentity { get; }
}
