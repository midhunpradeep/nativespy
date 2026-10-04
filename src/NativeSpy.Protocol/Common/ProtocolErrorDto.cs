namespace NativeSpy.Protocol.Common;

public sealed class ProtocolErrorDto
{
    public ProtocolErrorDto(
        ProtocolErrorCode code,
        string? message = null,
        string? diagnosticId = null)
    {
        Code = ContractValidation.RequireDefinedEnum(code, nameof(code));
        Message = ContractValidation.OptionalText(message, nameof(message));
        DiagnosticId = ContractValidation.OptionalIdentifier(diagnosticId, nameof(diagnosticId));
    }

    public ProtocolErrorCode Code { get; }

    public string? Message { get; }

    public string? DiagnosticId { get; }
}
