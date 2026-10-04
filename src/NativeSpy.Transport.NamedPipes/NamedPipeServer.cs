using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;

namespace NativeSpy.Transport.NamedPipes;

public sealed class NamedPipeServer : IAsyncDisposable
{
    private readonly NamedPipeServerOptions _options;
    private readonly object _gate = new();
    private NamedPipeServerStream? _listener;
    private int _disposed;

    public NamedPipeServer(NamedPipeServerOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public string PipeName => _options.PipeName;

    /// <summary>
    /// Creates the secured listener synchronously. A successful return means the
    /// explicit pipe ACL has been accepted by the operating system.
    /// </summary>
    public void Bind()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            _listener ??= CreateServerStream();
        }
    }

    public async Task<NamedPipeConnection> AcceptAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var stream = TakeOrCreateListener();
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
        NamedPipeServerStream? listener;
        lock (_gate)
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return ValueTask.CompletedTask;
            }

            listener = _listener;
            _listener = null;
        }

        listener?.Dispose();
        return ValueTask.CompletedTask;
    }

    private NamedPipeServerStream TakeOrCreateListener()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            return _listener is not null
                ? TakeListener()
                : CreateServerStream();
        }
    }

    private NamedPipeServerStream TakeListener()
    {
        var listener = _listener;
        _listener = null;
        return listener!;
    }

    private NamedPipeServerStream CreateServerStream()
    {
        var security = CreatePipeSecurity(_options.AllowedUserSid);
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

    private static PipeSecurity CreatePipeSecurity(SecurityIdentifier allowedUserSid)
    {
        var security = new PipeSecurity();
        security.SetOwner(allowedUserSid);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new PipeAccessRule(
            allowedUserSid,
            PipeAccessRights.ReadWrite | PipeAccessRights.Synchronize,
            AccessControlType.Allow));
        return security;
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(NamedPipeServer));
        }
    }
}
