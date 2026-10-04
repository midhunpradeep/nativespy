namespace NativeSpy.Protocol.Common;

public enum ProtocolErrorCode
{
    InvalidRequest,
    ProtocolMismatch,
    AuthenticationFailed,
    ProtocolViolation,
    UnsupportedOperation,
    SessionCapacityExceeded,
    ResponseTooLarge,
    InternalFailure
}
