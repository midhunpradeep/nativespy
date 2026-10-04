using System.IO.Pipes;

namespace NativeSpy.Transport.NamedPipes;

public static class NamedPipeClient
{
    public static async Task<NamedPipeConnection> ConnectAsync(
        NamedPipeClientOptions options,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        var stream = new NamedPipeClientStream(
            ".",
            options.PipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        try
        {
            await stream.ConnectAsync(timeout, cancellationToken).ConfigureAwait(false);
            return new NamedPipeConnection(stream, options.MaximumFrameBytes, isServer: false);
        }
        catch
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}
