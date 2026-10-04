using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;

namespace NativeSpy.Transport.NamedPipes;

public sealed class NamedPipeServer : IAsyncDisposable
{
    private readonly NamedPipeServerOptions _options;
    private int _disposed;

    public NamedPipeServer(NamedPipeServerOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<NamedPipeConnection> AcceptAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        var stream = CreateServerStream();
        try
        {
            await stream.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
            return new NamedPipeConnection(stream, _options.MaximumFrameBytes, isServer: true);
        }
        catch
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _disposed, 1);
        return ValueTask.CompletedTask;
    }

    private NamedPipeServerStream CreateServerStream()
    {
        var security = CreatePipeSecurity(_options.AllowedUserSid);
        try
        {
            return NamedPipeServerStreamAcl.Create(
                _options.PipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous,
                0,
                0,
                security,
                HandleInheritability.None,
                (PipeAccessRights)0);
        }
        catch
        {
            throw;
        }
    }

    private static PipeSecurity CreatePipeSecurity(SecurityIdentifier allowedUserSid)
    {
        var security = new PipeSecurity();
        try
        {
            security.SetOwner(allowedUserSid);
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            security.AddAccessRule(new PipeAccessRule(
                allowedUserSid,
                PipeAccessRights.ReadWrite | PipeAccessRights.Synchronize,
                AccessControlType.Allow));
            return security;
        }
        catch
        {
            throw;
        }
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(NamedPipeServer));
        }
    }
}
