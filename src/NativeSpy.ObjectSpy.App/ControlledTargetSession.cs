using System.Diagnostics;
using System.IO;
using System.Text;
using NativeSpy.Client.NamedPipes;
using NativeSpy.Protocol.Common;
using NativeSpy.Protocol.Json;
using NativeSpy.Transport.NamedPipes;

namespace NativeSpy.ObjectSpy.App;

internal sealed class ControlledTargetSession : IDisposable, IAsyncDisposable
{
    private const int ReadTimeoutMilliseconds = 10_000;
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(5);
    private readonly Process _process;
    private readonly Task<string> _stderrDrain;
    private int _disposed;

    private ControlledTargetSession(
        Process process,
        ProcessIdentityDto processIdentity,
        NamedPipeClientSession session,
        Task<string> stderrDrain)
    {
        _process = process;
        ProcessIdentity = processIdentity;
        Session = session;
        _stderrDrain = stderrDrain;
    }

    public ProcessIdentityDto ProcessIdentity { get; }

    public NamedPipeClientSession Session { get; }

    public static ControlledTargetSession Start()
    {
        var targetAssembly = Path.Combine(AppContext.BaseDirectory, "NativeSpy.TestTarget.WinForms.dll");
        if (!File.Exists(targetAssembly))
        {
            throw new FileNotFoundException("The controlled WinForms target was not copied beside ObjectSpy.", targetAssembly);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = AppContext.BaseDirectory
        };
        startInfo.ArgumentList.Add(targetAssembly);

        var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            process.Dispose();
            throw new InvalidOperationException("The controlled target could not be started.");
        }

        var stderrDrain = process.StandardError.ReadToEndAsync();
        try
        {
            var bootstrap = ProtocolJsonCodec.DeserializeBootstrap(
                Encoding.UTF8.GetBytes(ReadLine(process)));
            bootstrap.EnsureMessageKind();
            var identity = ProcessIdentityReader.ReadForProcessId(process.Id);
            if (bootstrap.TargetProcessIdentity.ProcessId != identity.ProcessId
                || !string.Equals(
                    bootstrap.TargetProcessIdentity.ProcessStartIdentity,
                    identity.ProcessStartIdentity,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The target bootstrap identity was not stable.");
            }

            var session = NamedPipeClientSession.ConnectAsync(
                    bootstrap,
                    identity,
                    cancellationToken: CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            return new ControlledTargetSession(process, identity, session, stderrDrain);
        }
        catch
        {
            TryKill(process);
            process.Dispose();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            await Session.DisposeAsync().AsTask().WaitAsync(ShutdownTimeout).ConfigureAwait(false);
        }
        catch
        {
            // The process lifetime below remains authoritative if the pipe is
            // already terminal or does not settle within the bounded window.
        }

        try
        {
            if (!_process.HasExited)
            {
                _process.CloseMainWindow();
            }
        }
        catch
        {
        }

        var exited = await WaitForExitAsync(ShutdownTimeout).ConfigureAwait(false);
        if (!exited)
        {
            await ForceTerminateAsync(_process).ConfigureAwait(false);
        }

        try
        {
            await _stderrDrain.WaitAsync(ShutdownTimeout).ConfigureAwait(false);
        }
        catch
        {
            // Do not let a diagnostic pipe prevent the owned process from
            // being reaped or the WPF dispatcher from completing shutdown.
        }

        _process.Dispose();
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private async Task<bool> WaitForExitAsync(TimeSpan timeout)
    {
        try
        {
            if (_process.HasExited)
            {
                return true;
            }

            using var cancellation = new CancellationTokenSource(timeout);
            await _process.WaitForExitAsync(cancellation.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch
        {
            return _process.HasExited;
        }
    }

    private static async Task ForceTerminateAsync(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
        }

        try
        {
            using var cancellation = new CancellationTokenSource(ShutdownTimeout);
            await process.WaitForExitAsync(cancellation.Token).ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private static string ReadLine(Process process)
    {
        using var timeout = new CancellationTokenSource(ReadTimeoutMilliseconds);
        try
        {
            return ReadBoundedLineAsync(
                    process.StandardOutput,
                    ProtocolWireConstants.MaximumBootstrapBytes,
                    timeout.Token)
                .GetAwaiter()
                .GetResult()
                ?? throw new InvalidOperationException("The target closed stdout before bootstrap.");
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            throw new TimeoutException("The target did not publish bootstrap.");
        }
    }

    private static async Task<string?> ReadBoundedLineAsync(
        StreamReader reader,
        int maximumCharacters,
        CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        var character = new char[1];
        while (true)
        {
            var count = await reader.ReadAsync(character.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (count == 0)
            {
                return builder.Length == 0 ? null : builder.ToString();
            }

            if (character[0] == '\n')
            {
                return builder.ToString();
            }

            if (character[0] == '\r')
            {
                continue;
            }

            if (builder.Length >= maximumCharacters)
            {
                throw new InvalidDataException("The target bootstrap exceeded its bound.");
            }

            builder.Append(character[0]);
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(ReadTimeoutMilliseconds);
            }
        }
        catch
        {
        }
    }
}
