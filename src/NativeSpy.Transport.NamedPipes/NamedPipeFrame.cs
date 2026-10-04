using System.Buffers;

namespace NativeSpy.Transport.NamedPipes;

public sealed class NamedPipeFrame : IDisposable
{
    private byte[]? _buffer;

    internal NamedPipeFrame(byte[] buffer, int length)
    {
        _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
        if (length < 0 || length > buffer.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        Length = length;
    }

    public int Length { get; }

    public ReadOnlyMemory<byte> Payload
    {
        get
        {
            var buffer = _buffer ?? throw new ObjectDisposedException(nameof(NamedPipeFrame));
            return buffer.AsMemory(0, Length);
        }
    }

    public void Dispose()
    {
        var buffer = Interlocked.Exchange(ref _buffer, null);
        if (buffer is null)
        {
            return;
        }

        Array.Clear(buffer, 0, Length);
        ArrayPool<byte>.Shared.Return(buffer);
    }
}

public sealed class NamedPipeFrameException : IOException
{
    public NamedPipeFrameException(string message)
        : base(message)
    {
    }
}
