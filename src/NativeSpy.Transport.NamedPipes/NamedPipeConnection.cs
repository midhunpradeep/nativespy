using System.Buffers;
using System.Buffers.Binary;
using System.IO.Pipes;
using System.Security.Principal;

namespace NativeSpy.Transport.NamedPipes;

public sealed class NamedPipeConnection : IAsyncDisposable
{
    private readonly Stream _stream;
    private readonly int _maximumFrameBytes;
    private readonly bool _isServer;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private int _disposed;

    internal NamedPipeConnection(Stream stream, int maximumFrameBytes, bool isServer)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _maximumFrameBytes = maximumFrameBytes;
        _isServer = isServer;
    }

    public bool IsServer => _isServer;

    public async Task<NamedPipeFrame?> ReadFrameAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var prefix = new byte[sizeof(uint)];
        var prefixRead = await ReadExactlyAsync(prefix, cancellationToken).ConfigureAwait(false);
        if (prefixRead == 0)
        {
            return null;
        }

        if (prefixRead != prefix.Length)
        {
            throw new NamedPipeFrameException("The named pipe closed during a frame length prefix.");
        }

        var length = BinaryPrimitives.ReadUInt32BigEndian(prefix);
        if (length == 0 || length > (uint)_maximumFrameBytes)
        {
            throw new NamedPipeFrameException("The named pipe frame length is outside the allowed range.");
        }

        var buffer = ArrayPool<byte>.Shared.Rent(checked((int)length));
        try
        {
            var read = await ReadExactlyAsync(
                    buffer.AsMemory(0, checked((int)length)),
                    cancellationToken)
                .ConfigureAwait(false);
            if (read != length)
            {
                throw new NamedPipeFrameException("The named pipe closed during a frame payload.");
            }

            var frame = new NamedPipeFrame(buffer, checked((int)length));
            buffer = null!;
            return frame;
        }
        finally
        {
            if (buffer is not null)
            {
                Array.Clear(buffer, 0, Math.Min(buffer.Length, checked((int)length)));
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }

    public async Task WriteFrameAsync(
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (payload.Length == 0 || payload.Length > _maximumFrameBytes)
        {
            throw new NamedPipeFrameException("The named pipe frame payload is outside the allowed range.");
        }

        var prefix = new byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32BigEndian(prefix, checked((uint)payload.Length));
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _stream.WriteAsync(prefix, cancellationToken).ConfigureAwait(false);
            await _stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public bool TryGetConnectedUserSid(out SecurityIdentifier? userSid)
    {
        userSid = null;
        if (!_isServer || _stream is not NamedPipeServerStream server)
        {
            return false;
        }

        try
        {
            SecurityIdentifier? captured = null;
            server.RunAsClient(() =>
            {
                using var identity = WindowsIdentity.GetCurrent(TokenAccessLevels.Query);
                captured = identity.User;
            });
            userSid = captured;
            return userSid is not null;
        }
        catch
        {
            return false;
        }
    }

    public void Abort()
    {
        DisposeCore();
    }

    public ValueTask DisposeAsync()
    {
        DisposeCore();
        return ValueTask.CompletedTask;
    }

    private async Task<int> ReadExactlyAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await _stream.ReadAsync(
                    buffer[total..],
                    cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(NamedPipeConnection));
        }
    }

    private void DisposeCore()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _stream.Dispose();
    }
}
