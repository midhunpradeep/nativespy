using NativeSpy.Protocol.Common;

namespace NativeSpy.Protocol.Correlation;

public sealed class CorrelationPolicyDto
{
    public CorrelationPolicyDto(
        CorrelationPolicyMode mode,
        int maxCandidates,
        int maxExternalNodes,
        int maxProviderNodes)
    {
        Mode = ContractValidation.RequireDefinedEnum(mode, nameof(mode));
        if (maxCandidates <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxCandidates), maxCandidates, "Candidate bound must be positive.");
        }

        if (maxExternalNodes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxExternalNodes), maxExternalNodes, "External node bound must be positive.");
        }

        if (maxProviderNodes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxProviderNodes), maxProviderNodes, "Provider node bound must be positive.");
        }

        MaxCandidates = maxCandidates;
        MaxExternalNodes = maxExternalNodes;
        MaxProviderNodes = maxProviderNodes;
    }

    public CorrelationPolicyMode Mode { get; }

    public int MaxCandidates { get; }

    public int MaxExternalNodes { get; }

    public int MaxProviderNodes { get; }
}
