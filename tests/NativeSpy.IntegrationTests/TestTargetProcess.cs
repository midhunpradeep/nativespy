using System.Diagnostics;
using System.Text;
using NativeSpy.Client.NamedPipes;
using NativeSpy.Protocol.Common;
using NativeSpy.Protocol.Json;
using NativeSpy.Transport.NamedPipes;

namespace NativeSpy.IntegrationTests;

internal sealed class TestTargetProcess : IDisposable
{
    private const int ReadTimeoutMilliseconds = 10_000;

    private readonly Process _process;
    private readonly ProcessIdentityDto _processIdentity;
    private readonly Task<string> _stderrDrain;
    private readonly NamedPipeClientSession _session;
    private bool _disposed;

    private TestTargetProcess(
        Process process,
        ProcessIdentityDto processIdentity,
        BootstrapDescriptorWire bootstrap,
        NamedPipeClientSession session,
        Task<string> stderrDrain)
    {
        _process = process;
        _processIdentity = processIdentity;
        Bootstrap = bootstrap;
        _session = session;
        _stderrDrain = stderrDrain;
    }

    public BootstrapDescriptorWire Bootstrap { get; }

    public ProcessIdentityDto ProcessIdentity => _processIdentity;

    public int ProcessId => _processIdentity.ProcessId;

    public NamedPipeClientSession Session => _session;

    public static TestTargetProcess Start()
    {
        var targetAssembly = Path.Combine(
            AppContext.BaseDirectory,
            "NativeSpy.TestTarget.WinForms.dll");
        if (!File.Exists(targetAssembly))
        {
            throw new FileNotFoundException("The MSBuild-copied WinForms test target was not found.", targetAssembly);
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
            throw new InvalidOperationException("The WinForms test target process could not be started.");
        }

        var stderrDrain = process.StandardError.ReadToEndAsync();
        NamedPipeClientSession? session = null;
        try
        {
            var bootstrapJson = ReadLine(process, ReadTimeoutMilliseconds);
            var bootstrap = ProtocolJsonCodec.DeserializeBootstrap(
                Encoding.UTF8.GetBytes(bootstrapJson));
            bootstrap.EnsureMessageKind();
            var expectedIdentity = ProcessIdentityReader.ReadForProcessId(process.Id);
            if (bootstrap.TargetProcessIdentity.ProcessId != expectedIdentity.ProcessId
                || !string.Equals(
                    bootstrap.TargetProcessIdentity.ProcessStartIdentity,
                    expectedIdentity.ProcessStartIdentity,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The bootstrap descriptor did not identify the independently observed target process.");
            }

            session = NamedPipeClientSession.ConnectAsync(
                    bootstrap,
                    expectedIdentity,
                    cancellationToken: CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            return new TestTargetProcess(process, expectedIdentity, bootstrap, session, stderrDrain);
        }
        catch
        {
            session?.DisposeAsync().GetAwaiter().GetResult();
            TryKill(process);
            process.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _session.DisposeAsync().GetAwaiter().GetResult();
        }
        catch
        {
            // The process teardown below is authoritative.
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
            // Fall through to the bounded process wait and kill fallback.
        }

        if (!_process.HasExited && !_process.WaitForExit(ReadTimeoutMilliseconds))
        {
            TryKill(_process);
        }

        _process.Dispose();
        _ = _stderrDrain.GetAwaiter().GetResult();
    }

    private static string ReadLine(Process process, int timeoutMilliseconds)
    {
        using var timeout = new CancellationTokenSource(timeoutMilliseconds);
        try
        {
            return ReadBoundedLineAsync(
                    process.StandardOutput,
                    ProtocolWireConstants.MaximumBootstrapBytes,
                    timeout.Token)
                .GetAwaiter()
                .GetResult()
                ?? throw new InvalidOperationException(
                    "The WinForms test target closed stdout before publishing bootstrap.");
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            throw new TimeoutException("The WinForms test target did not publish its bootstrap descriptor.");
        }
    }

    private static async Task<string?> ReadBoundedLineAsync(
        StreamReader reader,
        int maximumCharacters,
        CancellationToken cancellationToken)
    {
        var buffer = new StringBuilder();
        var character = new char[1];
        while (true)
        {
            var count = await reader.ReadAsync(character.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (count == 0)
            {
                return buffer.Length == 0 ? null : buffer.ToString();
            }

            if (character[0] == '\n')
            {
                return buffer.ToString();
            }

            if (character[0] == '\r')
            {
                continue;
            }

            if (buffer.Length >= maximumCharacters)
            {
                throw new InvalidDataException("The bootstrap descriptor exceeded its bound.");
            }

            buffer.Append(character[0]);
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
            // Best-effort cleanup for a failed test setup.
        }
    }
}
