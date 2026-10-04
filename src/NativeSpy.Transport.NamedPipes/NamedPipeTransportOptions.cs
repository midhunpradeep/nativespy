using System.Security.Principal;

namespace NativeSpy.Transport.NamedPipes;

public static class NamedPipeTransportLimits
{
    public const int DefaultMaximumFrameBytes = 1024 * 1024;
    public const int AbsoluteMaximumFrameBytes = 4 * 1024 * 1024;
}

public sealed class NamedPipeServerOptions
{
    public NamedPipeServerOptions(
        string pipeName,
        SecurityIdentifier allowedUserSid,
        int maximumFrameBytes = NamedPipeTransportLimits.DefaultMaximumFrameBytes)
    {
        PipeName = ValidatePipeName(pipeName, nameof(pipeName));
        AllowedUserSid = allowedUserSid ?? throw new ArgumentNullException(nameof(allowedUserSid));
        if (maximumFrameBytes <= 0 || maximumFrameBytes > NamedPipeTransportLimits.AbsoluteMaximumFrameBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumFrameBytes),
                maximumFrameBytes,
                "The frame limit must be positive and within the absolute transport ceiling.");
        }

        MaximumFrameBytes = maximumFrameBytes;
    }

    public string PipeName { get; }

    public SecurityIdentifier AllowedUserSid { get; }

    public int MaximumFrameBytes { get; }

    internal static string ValidatePipeName(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > 240
            || value.IndexOfAny(new[] { '\\', '/', ':' }) >= 0)
        {
            throw new ArgumentException("A local named-pipe name is required.", name);
        }

        return value;
    }
}

public sealed class NamedPipeClientOptions
{
    public NamedPipeClientOptions(
        string pipeName,
        int maximumFrameBytes = NamedPipeTransportLimits.DefaultMaximumFrameBytes)
    {
        if (string.IsNullOrWhiteSpace(pipeName))
        {
            throw new ArgumentException("A pipe name is required.", nameof(pipeName));
        }

        if (maximumFrameBytes <= 0 || maximumFrameBytes > NamedPipeTransportLimits.AbsoluteMaximumFrameBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumFrameBytes),
                maximumFrameBytes,
                "The frame limit must be positive and within the absolute transport ceiling.");
        }

        PipeName = NamedPipeServerOptions.ValidatePipeName(pipeName, nameof(pipeName));
        MaximumFrameBytes = maximumFrameBytes;
    }

    public string PipeName { get; }

    public int MaximumFrameBytes { get; }
}
