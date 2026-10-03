using NativeSpy.Protocol.Common;

namespace NativeSpy.Protocol.Correlation;

public sealed class ValidationCheckDto
{
    public ValidationCheckDto(
        string name,
        ValidationOutcome outcome,
        string? publicDetail = null)
    {
        Name = ContractValidation.RequiredIdentifier(name, nameof(name));
        Outcome = ContractValidation.RequireDefinedEnum(outcome, nameof(outcome));
        PublicDetail = ContractValidation.OptionalText(publicDetail, nameof(publicDetail));
    }

    public string Name { get; }

    public ValidationOutcome Outcome { get; }

    public string? PublicDetail { get; }
}
