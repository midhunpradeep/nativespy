using NativeSpy.Protocol.Common;

namespace NativeSpy.Protocol.Correlation;

public sealed class ProofStepDto
{
    public ProofStepDto(
        string name,
        ProofOutcome outcome,
        EvidenceKind evidenceKind,
        string? publicDetail = null)
    {
        Name = ContractValidation.RequiredIdentifier(name, nameof(name));
        Outcome = outcome;
        EvidenceKind = evidenceKind;
        PublicDetail = ContractValidation.OptionalText(publicDetail, nameof(publicDetail));
    }

    public string Name { get; }

    public ProofOutcome Outcome { get; }

    public EvidenceKind EvidenceKind { get; }

    public string? PublicDetail { get; }
}
