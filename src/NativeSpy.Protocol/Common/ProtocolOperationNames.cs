namespace NativeSpy.Protocol.Common;

/// <summary>
/// Canonical operation names that are part of the detached I4a wire contract.
/// </summary>
public static class ProtocolOperationNames
{
    public const string BeginCurrentHwnd = "correlation.winforms.beginCurrentHwnd";
    public const string RevalidateCurrentHwnd = "correlation.winforms.revalidateCurrentHwnd";
    public const string DescribeObject = "clr.describeObject";
    public const string ListMembers = "clr.listMembers";
    public const string ReadFieldValues = "clr.readFieldValues";
    public const string ReadPropertyValue = "clr.readPropertyValue";
    public const string Hello = "hello";
    public const string HelloResponse = "helloResponse";
    public const string Error = "error";
    public const string Request = "request";
    public const string Response = "response";
    public const string Shutdown = "shutdown";
    public const string ReservedSessionPrefix = "session.";

    private static readonly string[] ReservedExactNames =
    {
        Hello,
        HelloResponse,
        Error,
        Request,
        Response,
        Shutdown
    };

    public static IReadOnlyList<string> ReservedNames { get; } =
        Array.AsReadOnly(ReservedExactNames);

    public static IReadOnlyList<string> ProductionOperations { get; } =
        Array.AsReadOnly(new[]
        {
            BeginCurrentHwnd,
            RevalidateCurrentHwnd,
            DescribeObject,
            ListMembers,
            ReadFieldValues,
            ReadPropertyValue
        });

    public static bool IsCanonical(string? operationName)
    {
        if (string.IsNullOrWhiteSpace(operationName))
        {
            return false;
        }

        var segments = operationName!.Split('.');
        if (segments.Length < 2)
        {
            return false;
        }

        foreach (var segment in segments)
        {
            if (segment.Length == 0)
            {
                return false;
            }

            foreach (var character in segment)
            {
                if (!(character is >= 'a' and <= 'z')
                    && !(character is >= 'A' and <= 'Z')
                    && !(character is >= '0' and <= '9')
                    && character != '_')
                {
                    return false;
                }
            }
        }

        return true;
    }

    public static bool IsReserved(string? operationName)
    {
        return operationName is not null
            && (Array.IndexOf(ReservedExactNames, operationName) >= 0
                || operationName.StartsWith(ReservedSessionPrefix, StringComparison.Ordinal));
    }
}
