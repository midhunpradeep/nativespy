using NativeSpy.Protocol.Common;
using Xunit;

namespace NativeSpy.Protocol.Tests;

public sealed class ProtocolOperationNamesTests
{
    [Theory]
    [InlineData("hello")]
    [InlineData("helloResponse")]
    [InlineData("error")]
    [InlineData("request")]
    [InlineData("response")]
    [InlineData("shutdown")]
    [InlineData("session.close")]
    public void Protocol_names_are_reserved_ordinally(string operationName)
    {
        Assert.True(ProtocolOperationNames.IsReserved(operationName));
    }

    [Theory]
    [InlineData("Hello")]
    [InlineData("Session.close")]
    [InlineData("sessionx.close")]
    public void Reserved_names_are_case_sensitive_and_namespace_bounded(string operationName)
    {
        Assert.False(ProtocolOperationNames.IsReserved(operationName));
    }
}
