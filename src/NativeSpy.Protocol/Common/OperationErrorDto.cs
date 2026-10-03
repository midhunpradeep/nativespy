namespace NativeSpy.Protocol.Common;

public sealed class OperationErrorDto
{
    public OperationErrorDto(OperationErrorCode code, string? message = null)
    {
        Code = code;
        Message = ContractValidation.OptionalText(message, nameof(message));
    }

    public OperationErrorCode Code { get; }

    public string? Message { get; }
}
