using System.Diagnostics;
using System.Text;
using System.Text.Json;
using NativeSpy.Protocol.Common;
using NativeSpy.Protocol.Correlation;

namespace NativeSpy.IntegrationTests;

internal sealed class TestTargetProcess : IDisposable
{
    private const int ReadTimeoutMilliseconds = 10_000;
    private const int MaximumLineLength = 256 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly Process _process;
    private readonly object _gate = new();
    private readonly Task<string> _stderrDrain;
    private int _nextRequestId;
    private bool _disposed;

    private TestTargetProcess(Process process, int processId, Task<string> stderrDrain)
    {
        _process = process;
        ProcessId = processId;
        _stderrDrain = stderrDrain;
    }

    public int ProcessId { get; }

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
            RedirectStandardInput = true,
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
        try
        {
            var readyLine = ReadLine(process, ReadTimeoutMilliseconds);
            var ready = JsonSerializer.Deserialize<ReadyMessage>(readyLine, JsonOptions)
                ?? throw new InvalidOperationException("The test target returned an empty ready message.");
            if (!string.Equals(ready.Kind, "ready", StringComparison.Ordinal)
                || ready.ProcessId <= 0
                || ready.Hwnd == 0)
            {
                throw new InvalidOperationException("The test target returned an invalid ready message.");
            }

            return new TestTargetProcess(process, ready.ProcessId, stderrDrain);
        }
        catch
        {
            TryKill(process);
            process.Dispose();
            throw;
        }
    }

    public FrameworkCorrelationEvidenceDto Begin(ulong hwnd)
    {
        var response = Send(new BridgeRequest(
            NextRequestId(),
            "begin",
            hwnd,
            CandidateHandle: null));
        return ToEvidence(response);
    }

    public FrameworkCorrelationEvidenceDto Revalidate(ulong hwnd, HandleRefDto candidateHandle)
    {
        var response = Send(new BridgeRequest(
            NextRequestId(),
            "revalidate",
            hwnd,
            ToWire(candidateHandle)));
        return ToEvidence(response);
    }

    public string ReadText(HandleRefDto candidateHandle)
    {
        var response = Send(new BridgeRequest(
            NextRequestId(),
            "readText",
            Hwnd: null,
            CandidateHandle: ToWire(candidateHandle)));
        if (!response.Ok || response.Text is null)
        {
            throw new InvalidOperationException(response.Error ?? "The test target returned no Button.Text value.");
        }

        return response.Text;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        lock (_gate)
        {
            if (!_process.HasExited)
            {
                try
                {
                    Send(new BridgeRequest(NextRequestId(), "shutdown"));
                }
                catch
                {
                    TryKill(_process);
                }
            }
        }

        if (!_process.HasExited && !_process.WaitForExit(ReadTimeoutMilliseconds))
        {
            TryKill(_process);
        }

        _process.Dispose();
        _ = _stderrDrain.GetAwaiter().GetResult();
    }

    private BridgeResponse Send(BridgeRequest request)
    {
        lock (_gate)
        {
            if (_process.HasExited)
            {
                var diagnostics = _stderrDrain.IsCompleted
                    ? _stderrDrain.GetAwaiter().GetResult()
                    : string.Empty;
                throw new InvalidOperationException(
                    $"The WinForms test target exited before request '{request.Id}'. {diagnostics}");
            }

            var json = JsonSerializer.Serialize(request, JsonOptions);
            if (json.Length > MaximumLineLength)
            {
                throw new InvalidOperationException("The test bridge request exceeded its line-size bound.");
            }

            _process.StandardInput.WriteLine(json);
            _process.StandardInput.Flush();
            var responseLine = ReadLine(_process, ReadTimeoutMilliseconds);
            if (responseLine.Length > MaximumLineLength)
            {
                throw new InvalidOperationException("The test bridge response exceeded its line-size bound.");
            }

            return JsonSerializer.Deserialize<BridgeResponse>(responseLine, JsonOptions)
                ?? throw new InvalidOperationException("The test bridge returned an empty response.");
        }
    }

    private FrameworkCorrelationEvidenceDto ToEvidence(BridgeResponse response)
    {
        if (response.Evidence is null)
        {
            return new FrameworkCorrelationEvidenceDto(
                "winforms",
                ProcessId,
                candidateTarget: null,
                new[]
                {
                    new CorrelationEvidenceFactDto(
                        "ControlFromHandle",
                        ProofOutcome.NotAvailable,
                        EvidenceKind.Deterministic,
                        response.Error)
                },
                new[]
                {
                    new CorrelationValidationFactDto("CurrentHwndMatches", ValidationOutcome.NotAvailable, response.Error),
                    new CorrelationValidationFactDto("ControlLive", ValidationOutcome.NotAvailable, response.Error),
                    new CorrelationValidationFactDto("CandidateResolved", ValidationOutcome.NotAvailable, response.Error)
                },
                PassiveEffects("test-target.bridge"),
                Array.Empty<AdapterMetadataDto>(),
                new[] { new CorrelationLimitationDto("TestTargetBridgeOperationFailed", response.Error) },
                response.Ok
                    ? null
                    : new OperationErrorDto(OperationErrorCode.TargetOperationFailed, response.Error));
        }

        return ToEvidence(response.Evidence);
    }

    private static FrameworkCorrelationEvidenceDto ToEvidence(TargetEvidenceWire wire)
    {
        CorrelationTargetRefDto? candidateTarget = null;
        if (wire.CandidateHandle is not null)
        {
            var handle = FromWire(wire.CandidateHandle);
            TypeIdentityDto? typeIdentity = null;
            if (!string.IsNullOrWhiteSpace(wire.TypeFullName)
                && !string.IsNullOrWhiteSpace(wire.TypeId)
                && !string.IsNullOrWhiteSpace(wire.AssemblySimpleName)
                && !string.IsNullOrWhiteSpace(wire.BoundaryId))
            {
                typeIdentity = new TypeIdentityDto(
                    wire.TypeId,
                    wire.TypeFullName,
                    wire.AssemblySimpleName,
                    wire.BoundaryId,
                    isValueType: false,
                    Array.Empty<TypeRefDto>(),
                    Array.Empty<TypeRefDto>());
            }

            candidateTarget = new CorrelationTargetRefDto(
                CorrelationTargetKind.ManagedObject,
                managed: new ManagedObjectRefDto(handle, typeIdentity, handle.BoundaryId));
        }

        var error = wire.Error is null
            ? null
            : new OperationErrorDto((OperationErrorCode)wire.Error.Code, wire.Error.Message);
        return new FrameworkCorrelationEvidenceDto(
            wire.AdapterId,
            wire.ProcessId,
            candidateTarget,
            wire.EvidenceFacts.Select(fact => new CorrelationEvidenceFactDto(
                fact.Name,
                (ProofOutcome)fact.Outcome,
                (EvidenceKind)fact.EvidenceKind,
                fact.Detail)),
            wire.ValidationFacts.Select(fact => new CorrelationValidationFactDto(
                fact.Name,
                (ValidationOutcome)fact.Outcome,
                fact.Detail)),
            PassiveEffects("WinForms.Control.FromHandle"),
            Array.Empty<AdapterMetadataDto>(),
            Array.Empty<CorrelationLimitationDto>(),
            error);
    }

    private static CorrelationEffectSummaryDto PassiveEffects(params string[] operations)
    {
        return new CorrelationEffectSummaryDto(
            new[] { EffectCategory.Passive },
            FrameworkStateEffect.None,
            ApplicationCallbackEffect.None,
            Array.Empty<CallbackDetailDto>(),
            VisibleMutationEffect.NotRequested,
            operations);
    }

    private string NextRequestId() => $"request-{Interlocked.Increment(ref _nextRequestId)}";

    private static string ReadLine(Process process, int timeoutMilliseconds)
    {
        using var timeout = new CancellationTokenSource(timeoutMilliseconds);
        try
        {
            var line = ReadBoundedLineAsync(
                    process.StandardOutput,
                    MaximumLineLength,
                    timeout.Token)
                .GetAwaiter()
                .GetResult();
            return line
                ?? throw new EndOfStreamException("The test bridge closed stdout before responding.");
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            throw new TimeoutException("Timed out waiting for a test bridge response.");
        }
    }

    private static async Task<string?> ReadBoundedLineAsync(
        StreamReader reader,
        int maximumLength,
        CancellationToken cancellationToken)
    {
        var buffer = new StringBuilder();
        var character = new char[1];
        while (true)
        {
            var count = await reader
                .ReadAsync(character.AsMemory(), cancellationToken)
                .ConfigureAwait(false);
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

            if (buffer.Length == maximumLength)
            {
                throw new InvalidDataException("The response line exceeded the configured bound.");
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
                process.WaitForExit(2_000);
            }
        }
        catch
        {
            // Cleanup must not hide the original test/process failure.
        }
    }

    private static HandleWire ToWire(HandleRefDto handle)
    {
        return new HandleWire(
            handle.SessionId,
            handle.HandleId,
            handle.Generation,
            (int)handle.Kind,
            handle.BoundaryId);
    }

    private static HandleRefDto FromWire(HandleWire wire)
    {
        return new HandleRefDto(
            wire.SessionId,
            wire.HandleId,
            wire.Generation,
            (HandleKind)wire.Kind,
            wire.BoundaryId);
    }

    private sealed record ReadyMessage(string Kind, int ProcessId, ulong Hwnd);

    private sealed record BridgeRequest(
        string Id,
        string Command,
        ulong? Hwnd = null,
        HandleWire? CandidateHandle = null);

    private sealed record BridgeResponse(
        string Id,
        bool Ok,
        string? Error,
        TargetEvidenceWire? Evidence,
        string? Text,
        object? Reserved);

    private sealed record TargetEvidenceWire(
        string AdapterId,
        int ProcessId,
        HandleWire? CandidateHandle,
        string? TypeId,
        string? TypeFullName,
        string? AssemblySimpleName,
        string? BoundaryId,
        FactWire[] EvidenceFacts,
        ValidationWire[] ValidationFacts,
        ErrorWire? Error);

    private sealed record FactWire(string Name, int Outcome, int EvidenceKind, string? Detail);

    private sealed record ValidationWire(string Name, int Outcome, string? Detail);

    private sealed record ErrorWire(int Code, string? Message);

    private sealed record HandleWire(
        string SessionId,
        string HandleId,
        long Generation,
        int Kind,
        string? BoundaryId);
}
