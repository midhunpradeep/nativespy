using NativeSpy.Protocol.Common;

namespace NativeSpy.Protocol.Correlation;

public sealed class CorrelationTargetRefDto
{
    public CorrelationTargetRefDto(
        CorrelationTargetKind targetKind,
        ManagedObjectRefDto? managed = null,
        FrameworkEntityRefDto? framework = null,
        NativeEntityRefDto? native = null)
    {
        TargetKind = ContractValidation.RequireDefinedEnum(targetKind, nameof(targetKind));
        Managed = managed;
        Framework = framework;
        Native = native;

        var payloadCount = (managed is null ? 0 : 1)
            + (framework is null ? 0 : 1)
            + (native is null ? 0 : 1);
        if (payloadCount != 1)
        {
            throw new ArgumentException("A target reference must contain exactly one payload.");
        }

        var matches = targetKind switch
        {
            CorrelationTargetKind.ManagedObject => managed is not null && framework is null && native is null,
            CorrelationTargetKind.FrameworkEntity => framework is not null && managed is null && native is null,
            CorrelationTargetKind.NativeEntity => native is not null && managed is null && framework is null,
            _ => false
        };
        if (!matches)
        {
            throw new ArgumentException("The target payload must match TargetKind.", nameof(targetKind));
        }
    }

    public CorrelationTargetKind TargetKind { get; }

    public ManagedObjectRefDto? Managed { get; }

    public FrameworkEntityRefDto? Framework { get; }

    public NativeEntityRefDto? Native { get; }
}
