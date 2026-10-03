using NativeSpy.Protocol.Common;

namespace NativeSpy.Protocol.Correlation;

public sealed class CorrelationValidationDto
{
    public CorrelationValidationDto(
        IEnumerable<ValidationCheckDto> checks,
        IEnumerable<GenerationRefDto> generationRefs,
        bool revalidated)
    {
        Checks = ContractValidation.CopyRequired(checks, nameof(checks));
        GenerationRefs = ContractValidation.CopyRequired(generationRefs, nameof(generationRefs));
        Revalidated = revalidated;
    }

    public IReadOnlyList<ValidationCheckDto> Checks { get; }

    public IReadOnlyList<GenerationRefDto> GenerationRefs { get; }

    public bool Revalidated { get; }
}
