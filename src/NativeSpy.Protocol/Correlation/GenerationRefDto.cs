using NativeSpy.Protocol.Common;

namespace NativeSpy.Protocol.Correlation;

public sealed class GenerationRefDto
{
    public GenerationRefDto(string adapterId, string kind, string value, string? scopeId = null)
    {
        AdapterId = ContractValidation.RequiredIdentifier(adapterId, nameof(adapterId));
        Kind = ContractValidation.RequiredIdentifier(kind, nameof(kind));
        Value = ContractValidation.RequiredIdentifier(value, nameof(value));
        ScopeId = ContractValidation.OptionalIdentifier(scopeId, nameof(scopeId));
    }

    public string AdapterId { get; }

    public string Kind { get; }

    public string? ScopeId { get; }

    public string Value { get; }
}
