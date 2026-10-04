using NativeSpy.Protocol.Common;

namespace NativeSpy.Client.NamedPipes;

public static class NamedPipeOperationNames
{
    public const string BeginCurrentHwnd = ProtocolOperationNames.BeginCurrentHwnd;
    public const string RevalidateCurrentHwnd = ProtocolOperationNames.RevalidateCurrentHwnd;
    public const string DescribeObject = ProtocolOperationNames.DescribeObject;
    public const string ListMembers = ProtocolOperationNames.ListMembers;
    public const string ReadFieldValues = ProtocolOperationNames.ReadFieldValues;
    public const string ReadPropertyValue = ProtocolOperationNames.ReadPropertyValue;
}
