using System.Diagnostics;
using Xunit;
using Xunit.Sdk;

namespace NativeSpy.IntegrationTests;

[Collection("NativeSpy live UI")]
public sealed class ObjectSpyApplicationShutdownTests
{
    [Fact]
    public async Task ObjectSpy_executable_closes_without_forced_process_termination()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw SkipException.ForSkip("The executable shutdown proof requires Windows WPF.");
        }

        var applicationPath = Path.Combine(
            AppContext.BaseDirectory,
            "NativeSpy.ObjectSpy.App.exe");
        if (!File.Exists(applicationPath))
        {
            throw SkipException.ForSkip($"The built ObjectSpy executable was not copied to '{applicationPath}'.");
        }

        var existingTargetProcessIds = Process.GetProcessesByName("dotnet")
            .Select(process =>
            {
                using (process)
                {
                    return process.Id;
                }
            })
            .ToHashSet();
        using var application = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = applicationPath,
                WorkingDirectory = Path.GetDirectoryName(applicationPath)!,
                UseShellExecute = false,
                CreateNoWindow = false
            }
        };
        Assert.True(application.Start());

        Process? target = null;
        var forcedCleanup = false;
        try
        {
            target = await WaitForControlledTargetAsync(
                existingTargetProcessIds,
                TimeSpan.FromSeconds(15));
            Assert.NotNull(target);
            Assert.True(await WaitForMainWindowAsync(application, TimeSpan.FromSeconds(15)));
            Assert.True(application.CloseMainWindow());
            Assert.True(
                await WaitForExitAsync(application, TimeSpan.FromSeconds(15)),
                "ObjectSpy did not exit within the bounded normal-shutdown window.");
            Assert.True(
                await WaitForExitAsync(target!, TimeSpan.FromSeconds(15)),
                "The controlled target did not exit after ObjectSpy shutdown.");
        }
        finally
        {
            if (!application.HasExited)
            {
                forcedCleanup = true;
                TryKill(application);
            }

            if (target is not null && !target.HasExited)
            {
                forcedCleanup = true;
                TryKill(target);
            }

            target?.Dispose();
        }

        Assert.False(forcedCleanup, "The test required forced process cleanup.");
    }

    private static async Task<Process?> WaitForControlledTargetAsync(
        IReadOnlySet<int> existingProcessIds,
        TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        while (!cancellation.IsCancellationRequested)
        {
            foreach (var process in Process.GetProcessesByName("dotnet"))
            {
                try
                {
                    if (!existingProcessIds.Contains(process.Id)
                        && process.MainWindowHandle != IntPtr.Zero
                        && string.Equals(
                            process.MainWindowTitle,
                            "NativeSpy Test Target",
                            StringComparison.Ordinal))
                    {
                        return process;
                    }
                }
                catch
                {
                    process.Dispose();
                    continue;
                }

                process.Dispose();
            }

            try
            {
                await Task.Delay(100, cancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        return null;
    }

    private static async Task<bool> WaitForMainWindowAsync(Process process, TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        while (!cancellation.IsCancellationRequested)
        {
            try
            {
                if (process.HasExited)
                {
                    return false;
                }

                process.Refresh();
                if (process.MainWindowHandle != IntPtr.Zero)
                {
                    return true;
                }
            }
            catch
            {
                return false;
            }

            try
            {
                await Task.Delay(100, cancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        return false;
    }

    private static async Task<bool> WaitForExitAsync(Process process, TimeSpan timeout)
    {
        if (process.HasExited)
        {
            return true;
        }

        using var cancellation = new CancellationTokenSource(timeout);
        try
        {
            await process.WaitForExitAsync(cancellation.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5_000);
            }
        }
        catch
        {
        }
    }
}
