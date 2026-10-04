using System.Drawing;
using System.Runtime.InteropServices;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using NativeSpy.Protocol.Common;

namespace NativeSpy.FlaUI;

/// <summary>
/// UIA3 session whose automation and live elements are owned by one dedicated MTA worker.
/// </summary>
public sealed class FlaUiAutomationSession : IDisposable
{
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(5);
    private readonly FlaUiMtaExecutor _executor;
    private readonly Application _application;
    private readonly UIA3Automation _automation;
    private readonly int _processId;
    private readonly object _gate = new();
    private bool _quarantineReserved;
    private int _resourcesDisposed;
    private int _disposed;

    private FlaUiAutomationSession(
        FlaUiMtaExecutor executor,
        Application application,
        UIA3Automation automation,
        int processId)
    {
        _executor = executor;
        _application = application;
        _automation = automation;
        _processId = processId;
    }

    public int ProcessId => _processId;

    public bool IsPoisoned => _executor.IsPoisoned;

    public static FlaUiAutomationSession Attach(int processId)
    {
        if (processId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(processId), processId, "ProcessId must be positive.");
        }

        FlaUiAutomationSession? session = null;
        var executor = new FlaUiMtaExecutor(() => session?.OnExecutorExited());
        try
        {
            var resources = executor.InvokeWithTimeoutAsync(
                    () =>
                    {
                        var application = Application.Attach(processId);
                        var automation = new UIA3Automation
                        {
                            TransactionTimeout = TimeSpan.FromSeconds(2),
                            ConnectionTimeout = TimeSpan.FromSeconds(2)
                        };
                        return (application, automation);
                    },
                    OperationTimeout,
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            session = new FlaUiAutomationSession(executor, resources.application, resources.automation, processId);
            return session;
        }
        catch
        {
            executor.Dispose();
            throw;
        }
    }

    public static FlaUiAutomationSession Attach(ProcessIdentityDto targetProcessIdentity)
    {
        if (targetProcessIdentity is null)
        {
            throw new ArgumentNullException(nameof(targetProcessIdentity));
        }

        return Attach(targetProcessIdentity.ProcessId);
    }

    public FlaUiExternalSource FindByAutomationId(string automationId)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        if (string.IsNullOrWhiteSpace(automationId))
        {
            throw new ArgumentException("AutomationId must not be blank.", nameof(automationId));
        }

        return Execute(
            () =>
            {
                var window = _application.GetMainWindow(_automation, TimeSpan.FromSeconds(10))
                    ?? throw new InvalidOperationException("The target process did not expose a main UI Automation window.");
                var element = window.FindFirstDescendant(automationId)
                    ?? throw new InvalidOperationException($"No UI Automation element matched AutomationId '{automationId}'.");
                return new FlaUiExternalSource(this, _automation, element, _processId);
            });
    }

    public async Task<FlaUiSelectionObservation> PreviewFromPointAsync(
        Point screenPoint,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        return await ExecuteAsync(
                () => CreateObservation(_automation.FromPoint(screenPoint), screenPoint),
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<FlaUiFrozenSelection> FreezeFromPointAsync(
        Point screenPoint,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        return await ExecuteAsync(
                () =>
                {
                    var element = _automation.FromPoint(screenPoint)
                        ?? throw new InvalidOperationException("UI Automation returned no element at the requested point.");
                    var observation = CreateObservation(element, screenPoint);
                    if (!observation.IsAvailable
                        || observation.CandidateProcessId != _processId)
                    {
                        throw new InvalidOperationException(
                            observation.Limitation ?? "The UI Automation candidate did not belong to the target process.");
                    }

                    return new FlaUiFrozenSelection(
                        new FlaUiExternalSource(this, _automation, element, _processId),
                        observation);
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    internal Task<T> ExecuteAsync<T>(Func<T> callback, CancellationToken cancellationToken)
    {
        return ExecuteWithTimeoutAsync(callback, cancellationToken);
    }

    internal T Execute<T>(Func<T> callback)
    {
        return ExecuteWithTimeoutAsync(callback, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    internal void MarkPoisoned()
    {
        lock (_gate)
        {
            if (_quarantineReserved)
            {
                return;
            }

            if (!FlaUiQuarantineBudget.TryReserve())
            {
                throw new FlaUiSessionException("The UI Automation quarantine budget is exhausted.");
            }

            _quarantineReserved = true;
            _executor.Poison();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        if (!_executor.IsPoisoned)
        {
            try
            {
                _executor.InvokeWithTimeoutAsync(
                        () =>
                        {
                            DisposeResourcesOnWorker();
                            return true;
                        },
                        OperationTimeout,
                        CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
            }
            catch
            {
                try
                {
                    MarkPoisoned();
                }
                catch
                {
                    // The session is already terminal; never cross-thread dispose.
                }
            }
        }

        _executor.Dispose();
    }

    private async Task<T> ExecuteWithTimeoutAsync<T>(
        Func<T> callback,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _executor
                .InvokeWithTimeoutAsync(callback, OperationTimeout, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (FlaUiSessionException)
        {
            if (!_executor.IsPoisoned)
            {
                throw;
            }

            try
            {
                MarkPoisoned();
            }
            catch (FlaUiSessionException)
            {
                throw;
            }

            throw;
        }
    }

    private FlaUiSelectionObservation CreateObservation(
        AutomationElement? element,
        Point screenPoint)
    {
        if (element is null)
        {
            return new FlaUiSelectionObservation(
                screenPoint,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                false,
                "No UI Automation element was available at the requested point.");
        }

        var candidateProcessId = element.Properties.ProcessId.ValueOrDefault;
        var candidateHwndValue = element.Properties.NativeWindowHandle.ValueOrDefault;
        ulong? candidateHwnd = candidateHwndValue > 0
            ? unchecked((ulong)(uint)candidateHwndValue)
            : null;
        var rootHwnd = candidateHwnd is null ? null : TryGetRootHwnd(candidateHwnd.Value);
        var rectangle = element.Properties.BoundingRectangle.ValueOrDefault;
        var runtimeId = element.Properties.RuntimeId.ValueOrDefault;
        var isAvailable = element.IsAvailable;
        var processMatches = candidateProcessId == _processId;
        var limitation = !isAvailable
            ? "The UI Automation element is unavailable."
            : candidateProcessId <= 0
                ? "The UI Automation element did not expose a process ID."
                : !processMatches
                    ? "The UI Automation element belongs to a different process ID."
                    : null;

        return new FlaUiSelectionObservation(
            screenPoint,
            candidateProcessId > 0 ? candidateProcessId : null,
            candidateHwnd,
            rootHwnd,
            rectangle == Rectangle.Empty ? null : rectangle,
            element.Properties.Name.ValueOrDefault,
            element.Properties.AutomationId.ValueOrDefault,
            element.Properties.ClassName.ValueOrDefault,
            element.Properties.ControlType.ValueOrDefault.ToString(),
            runtimeId,
            isAvailable && processMatches,
            limitation);
    }

    private static ulong? TryGetRootHwnd(ulong hwnd)
    {
        if (hwnd == 0 || hwnd > long.MaxValue)
        {
            return null;
        }

        var root = GetAncestor(new IntPtr(unchecked((long)hwnd)), 2);
        return root == IntPtr.Zero ? null : unchecked((ulong)root.ToInt64());
    }

    private void OnExecutorExited()
    {
        DisposeResourcesOnWorker();
        lock (_gate)
        {
            if (_quarantineReserved)
            {
                _quarantineReserved = false;
                FlaUiQuarantineBudget.Release();
            }
        }
    }

    private void DisposeResourcesOnWorker()
    {
        if (Interlocked.Exchange(ref _resourcesDisposed, 1) != 0)
        {
            return;
        }

        try
        {
            _automation.Dispose();
        }
        catch
        {
        }

        try
        {
            _application.Dispose();
        }
        catch
        {
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
}
