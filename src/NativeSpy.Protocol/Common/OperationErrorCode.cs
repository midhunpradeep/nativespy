namespace NativeSpy.Protocol.Common;

public enum OperationErrorCode
{
    SessionClosed,
    InvalidHandle,
    StaleHandle,
    ExecutionContextUnavailable,
    WrongExecutionContext,
    TargetTimeout,
    TargetOperationFailed,
    UnsupportedOperation,
    CapabilityUnavailable,
    DisabledByPolicy,
    SerializationLimit,
    TargetExited,
    ObjectCollected,
    RuntimeUnavailable
}
