using System.Security.Cryptography;
using System.Security.Principal;
using NativeSpy.Protocol.Common;
using NativeSpy.Protocol.Json;
using NativeSpy.Transport.NamedPipes;

namespace NativeSpy.Agent.Host;

public sealed class AgentHostOptions : IDisposable
{
    private byte[]? _bootstrapNonce;

    public AgentHostOptions(
        string pipeName,
        ReadOnlySpan<byte> bootstrapNonce,
        ProcessIdentityDto targetProcessIdentity,
        SecurityIdentifier allowedUserSid,
        int minSupportedVersion = ProtocolWireConstants.CurrentProtocolVersion,
        int maxSupportedVersion = ProtocolWireConstants.CurrentProtocolVersion,
        TimeSpan? bootstrapLifetime = null,
        TimeSpan? handshakeTimeout = null,
        ulong defaultBudgetMs = 10_000,
        ulong maxBudgetMs = 60_000,
        int maximumFrameBytes = ProtocolWireConstants.DefaultMaximumFrameBytes,
        int maximumOutstandingRequests = 8)
    {
        if (string.IsNullOrWhiteSpace(pipeName)
            || pipeName.Length > 240
            || pipeName.IndexOfAny(new[] { '\\', '/', ':' }) >= 0)
        {
            throw new ArgumentException("A local named-pipe name is required.", nameof(pipeName));
        }

        if (bootstrapNonce.Length != 32)
        {
            throw new ArgumentException("The bootstrap nonce must contain 32 bytes.", nameof(bootstrapNonce));
        }

        TargetProcessIdentity = targetProcessIdentity ?? throw new ArgumentNullException(nameof(targetProcessIdentity));
        AllowedUserSid = allowedUserSid ?? throw new ArgumentNullException(nameof(allowedUserSid));
        if (minSupportedVersion <= 0 || maxSupportedVersion < minSupportedVersion)
        {
            throw new ArgumentOutOfRangeException(nameof(minSupportedVersion), "The supported protocol range is invalid.");
        }

        if (defaultBudgetMs == 0
            || maxBudgetMs < defaultBudgetMs
            || maxBudgetMs > (ulong)TimeSpan.MaxValue.TotalMilliseconds)
        {
            throw new ArgumentOutOfRangeException(nameof(defaultBudgetMs), "The operation budget limits are invalid.");
        }

        BootstrapLifetime = bootstrapLifetime ?? TimeSpan.FromMinutes(1);
        HandshakeTimeout = handshakeTimeout ?? TimeSpan.FromSeconds(5);
        if (BootstrapLifetime <= TimeSpan.Zero || HandshakeTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(bootstrapLifetime), "Host lifecycle timeouts must be positive.");
        }

        if (maximumFrameBytes <= 0 || maximumFrameBytes > ProtocolWireConstants.DefaultMaximumFrameBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumFrameBytes));
        }

        if (maximumOutstandingRequests <= 0 || maximumOutstandingRequests > 8)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumOutstandingRequests),
                "The Host supports at most eight outstanding requests per session.");
        }

        PipeName = pipeName;
        _bootstrapNonce = bootstrapNonce.ToArray();
        MinSupportedVersion = minSupportedVersion;
        MaxSupportedVersion = maxSupportedVersion;
        DefaultBudgetMs = defaultBudgetMs;
        MaxBudgetMs = maxBudgetMs;
        MaximumFrameBytes = maximumFrameBytes;
        MaximumOutstandingRequests = maximumOutstandingRequests;
    }

    public string PipeName { get; }

    public ProcessIdentityDto TargetProcessIdentity { get; }

    public SecurityIdentifier AllowedUserSid { get; }

    public int MinSupportedVersion { get; }

    public int MaxSupportedVersion { get; }

    public TimeSpan BootstrapLifetime { get; }

    public TimeSpan HandshakeTimeout { get; }

    public ulong DefaultBudgetMs { get; }

    public ulong MaxBudgetMs { get; }

    public int MaximumFrameBytes { get; }

    public int MaximumOutstandingRequests { get; }

    internal byte[] BootstrapNonceBytes
    {
        get
        {
            var nonce = _bootstrapNonce;
            if (nonce is null)
            {
                throw new ObjectDisposedException(nameof(AgentHostOptions));
            }

            return nonce;
        }
    }

    internal void ClearBootstrapNonce()
    {
        var nonce = Interlocked.Exchange(ref _bootstrapNonce, null);
        if (nonce is not null)
        {
            CryptographicOperations.ZeroMemory(nonce);
        }
    }

    public BootstrapDescriptorWire CreateBootstrapDescriptor()
    {
        return new BootstrapDescriptorWire
        {
            Kind = "nativespy.bootstrap",
            DescriptorVersion = 1,
            PipeName = PipeName,
            BootstrapNonce = Convert.ToBase64String(BootstrapNonceBytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_'),
            TargetProcessIdentity = new ProcessIdentityWire
            {
                ProcessId = TargetProcessIdentity.ProcessId,
                ProcessStartIdentity = TargetProcessIdentity.ProcessStartIdentity
            },
            MinSupportedVersion = MinSupportedVersion,
            MaxSupportedVersion = MaxSupportedVersion
        };
    }

    public void Dispose()
    {
        ClearBootstrapNonce();
    }

    public static AgentHostOptions CreateDefault(
        ProcessIdentityDto targetProcessIdentity,
        SecurityIdentifier allowedUserSid)
    {
        var pipeBytes = RandomNumberGenerator.GetBytes(16);
        var pipeName = "nativespy-" + Convert.ToHexString(pipeBytes).ToLowerInvariant();
        var nonce = RandomNumberGenerator.GetBytes(32);
        return new AgentHostOptions(pipeName, nonce, targetProcessIdentity, allowedUserSid);
    }
}
