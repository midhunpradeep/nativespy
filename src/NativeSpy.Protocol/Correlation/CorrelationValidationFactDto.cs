using NativeSpy.Protocol.Common;

namespace NativeSpy.Protocol.Correlation;

/// <summary>
/// A detached lifecycle/currentness observation reported by an adapter.
/// </summary>
public sealed class CorrelationValidationFactDto
{
    public CorrelationValidationFactDto(
        string name,
        ValidationOutcome outcome,
        string? detail = null)
    {
        Name = ContractValidation.RequiredIdentifier(name, nameof(name));
        Outcome = ContractValidation.RequireDefinedEnum(outcome, nameof(outcome));
        Detail = ContractValidation.OptionalText(detail, nameof(detail));
    }

    public string Name { get; }

    public ValidationOutcome Outcome { get; }

    public string? Detail { get; }
}
