using System.Diagnostics;
using NativeSpy.Protocol.Common;
using NativeSpy.Transport.NamedPipes;

namespace NativeSpy.IntegrationTests;

internal sealed class WpfTestTargetProcess : IDisposable
{
    private const int StartupTimeoutMilliseconds = 10_000;
    private const int ShutdownTimeoutMilliseconds = 10_000;

    private readonly Process _process;
    private bool _disposed;

    private WpfTestTargetProcess(Process process, IntPtr mainWindowHandle)
    {
        _process = process;
        MainWindowHandle = mainWindowHandle;
    }

    public int ProcessId => _process.Id;

    public ProcessIdentityDto ProcessIdentity => ProcessIdentityReader.ReadForProcessId(ProcessId);

    public IntPtr MainWindowHandle { get; }

    public static WpfTestTargetProcess Start()
    {
        var targetAssembly = Path.Combine(
            AppContext.BaseDirectory,
            "NativeSpy.TestTarget.Wpf.dll");
        if (!File.Exists(targetAssembly))
        {
            throw new FileNotFoundException("The MSBuild-copied WPF test target was not found.", targetAssembly);
        }

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                WorkingDirectory = AppContext.BaseDirectory,
                UseShellExecute = false,
                CreateNoWindow = false
            }
        };
        process.StartInfo.ArgumentList.Add(targetAssembly);
        if (!process.Start())
        {
            process.Dispose();
            throw new InvalidOperationException("The WPF test target process could not be started.");
        }

        try
        {
            var mainWindowHandle = WaitForMainWindow(process, StartupTimeoutMilliseconds);
            return new WpfTestTargetProcess(process, mainWindowHandle);
        }
        catch
        {
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
            if (!_process.HasExited)
            {
                _process.CloseMainWindow();
            }

            if (!_process.HasExited && !_process.WaitForExit(ShutdownTimeoutMilliseconds))
            {
                TryKill(_process);
            }
        }
        finally
        {
            _process.Dispose();
        }
    }

    private static IntPtr WaitForMainWindow(Process process, int timeoutMilliseconds)
    {
        using var timeout = new CancellationTokenSource(timeoutMilliseconds);
        while (!timeout.IsCancellationRequested)
        {
            if (process.HasExited)
            {
                throw new InvalidOperationException("The WPF test target exited before showing its window.");
            }

            process.Refresh();
            if (process.MainWindowHandle != IntPtr.Zero)
            {
                return process.MainWindowHandle;
            }

            Thread.Sleep(50);
        }

        throw new TimeoutException("The WPF test target did not show its main window.");
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(ShutdownTimeoutMilliseconds);
            }
        }
        catch
        {
        }
    }
}
