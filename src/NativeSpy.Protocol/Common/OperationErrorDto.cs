namespace NativeSpy.Protocol.Common;

public sealed class OperationErrorDto
{
    public OperationErrorDto(OperationErrorCode code, string? message = null)
    {
        Code = ContractValidation.RequireDefinedEnum(code, nameof(code));
        Message = ContractValidation.OptionalText(message, nameof(message));
    }

    public OperationErrorCode Code { get; }

    public string? Message { get; }
}
