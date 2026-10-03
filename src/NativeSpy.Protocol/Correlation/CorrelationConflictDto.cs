using NativeSpy.Protocol.Common;

namespace NativeSpy.Protocol.Correlation;

public sealed class CorrelationConflictDto
{
    public CorrelationConflictDto(string code, string? detail = null)
    {
        Code = ContractValidation.RequiredIdentifier(code, nameof(code));
        Detail = ContractValidation.OptionalText(detail, nameof(detail));
    }

    public string Code { get; }

    public string? Detail { get; }
}
