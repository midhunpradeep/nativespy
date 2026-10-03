using NativeSpy.Protocol.Common;

namespace NativeSpy.Protocol.Correlation;

/// <summary>
/// A detached observation reported by an adapter before Client normalization.
/// </summary>
public sealed class CorrelationEvidenceFactDto
{
    public CorrelationEvidenceFactDto(
        string name,
        ProofOutcome outcome,
        EvidenceKind evidenceKind,
        string? detail = null)
    {
        Name = ContractValidation.RequiredIdentifier(name, nameof(name));
        Outcome = ContractValidation.RequireDefinedEnum(outcome, nameof(outcome));
        EvidenceKind = ContractValidation.RequireDefinedEnum(evidenceKind, nameof(evidenceKind));
        Detail = ContractValidation.OptionalText(detail, nameof(detail));
    }

    public string Name { get; }

    public ProofOutcome Outcome { get; }

    public EvidenceKind EvidenceKind { get; }

    public string? Detail { get; }
}
