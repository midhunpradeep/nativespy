using NativeSpy.Protocol.Clr;
using NativeSpy.Protocol.Common;
using NativeSpy.Protocol.Correlation;

namespace NativeSpy.Client.ClrInspection;

public sealed class ClrInspectionClientResult<T>
    where T : class
{
    private ClrInspectionClientResult(T? value, OperationErrorDto? error)
    {
        Value = value;
        Error = error;
    }

    public T? Value { get; }

    public OperationErrorDto? Error { get; }

    public bool IsSuccess => Value is not null;

    public static ClrInspectionClientResult<T> Success(T value)
    {
        return new ClrInspectionClientResult<T>(
            value ?? throw new ArgumentNullException(nameof(value)),
            null);
    }

    public static ClrInspectionClientResult<T> Failure(OperationErrorDto error)
    {
        return new ClrInspectionClientResult<T>(
            null,
            error ?? throw new ArgumentNullException(nameof(error)));
    }
}

public interface IClrInspectionPort
{
    Task<ClrInspectionClientResult<DescribeObjectResponseDto>> DescribeObjectAsync(
        ManagedObjectRefDto @object,
        CancellationToken cancellationToken);

    Task<ClrInspectionClientResult<ListMembersResponseDto>> ListMembersAsync(
        ManagedObjectRefDto @object,
        int pageSize,
        ClrMemberKindFilter filter,
        string? continuationToken,
        CancellationToken cancellationToken);

    Task<ClrInspectionClientResult<ReadFieldValuesResponseDto>> ReadFieldValuesAsync(
        ManagedObjectRefDto @object,
        IReadOnlyList<MemberRefDto> members,
        CancellationToken cancellationToken);

    Task<ClrInspectionClientResult<ReadPropertyValueResponseDto>> ReadPropertyValueAsync(
        ManagedObjectRefDto @object,
        MemberRefDto member,
        CancellationToken cancellationToken);
}
