using NativeSpy.Protocol.Common;

namespace NativeSpy.Protocol.Correlation;

public sealed class CorrelationProofSummaryDto
{
    public CorrelationProofSummaryDto(
        CorrelationProofMethod method,
        IEnumerable<ProofStepDto> steps,
        IEnumerable<ValidationCheckDto> validationDetails,
        IEnumerable<CorrelationLimitationDto> limitations,
        bool revalidated)
    {
        Method = ContractValidation.RequireDefinedEnum(method, nameof(method));
        Steps = ContractValidation.CopyRequired(steps, nameof(steps));
        ValidationDetails = ContractValidation.CopyRequired(validationDetails, nameof(validationDetails));
        Limitations = ContractValidation.CopyRequired(limitations, nameof(limitations));
        Revalidated = revalidated;
    }

    public CorrelationProofMethod Method { get; }

    public IReadOnlyList<ProofStepDto> Steps { get; }

    public IReadOnlyList<ValidationCheckDto> ValidationDetails { get; }

    public IReadOnlyList<CorrelationLimitationDto> Limitations { get; }

    public bool Revalidated { get; }

}
