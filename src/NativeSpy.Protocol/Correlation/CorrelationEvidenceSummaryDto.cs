using NativeSpy.Protocol.Common;

namespace NativeSpy.Protocol.Correlation;

public sealed class CorrelationEvidenceSummaryDto
{
    public CorrelationEvidenceSummaryDto(
        string name,
        EvidenceKind evidenceKind,
        string? detail = null)
    {
        Name = ContractValidation.RequiredIdentifier(name, nameof(name));
        EvidenceKind = evidenceKind;
        Detail = ContractValidation.OptionalText(detail, nameof(detail));
    }

    public string Name { get; }

    public EvidenceKind EvidenceKind { get; }

    public string? Detail { get; }
}
