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

    private TestTargetProcess(
        Process process,
        int processId,
        ulong mainWindowHwnd,
        Task<string> stderrDrain)
    {
        _process = process;
        ProcessId = processId;
        MainWindowHwnd = mainWindowHwnd;
        _stderrDrain = stderrDrain;
    }

    public int ProcessId { get; }

    public ulong MainWindowHwnd { get; }

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

            return new TestTargetProcess(process, ready.ProcessId, ready.Hwnd, stderrDrain);
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
        var error = wire.Error is null
            ? null
            : new OperationErrorDto((OperationErrorCode)wire.Error.Code, wire.Error.Message);
        return new FrameworkCorrelationEvidenceDto(
            wire.AdapterId,
            wire.ProcessId,
            FromWire(wire.CandidateTarget),
            wire.EvidenceFacts.Select(fact => new CorrelationEvidenceFactDto(
                fact.Name,
                (ProofOutcome)fact.Outcome,
                (EvidenceKind)fact.EvidenceKind,
                fact.Detail)),
            wire.ValidationFacts.Select(fact => new CorrelationValidationFactDto(
                fact.Name,
                (ValidationOutcome)fact.Outcome,
                fact.Detail)),
            FromWire(wire.Effects),
            wire.AdapterMetadata.Select(FromWire),
            wire.Limitations.Select(FromWire),
            error);
    }

    private static CorrelationTargetRefDto? FromWire(TargetWire? wire)
    {
        if (wire is null)
        {
            return null;
        }

        if (wire.Managed is null)
        {
            throw new InvalidOperationException("The I1 target wire omitted its managed target payload.");
        }

        return new CorrelationTargetRefDto(
            (CorrelationTargetKind)wire.TargetKind,
            managed: FromWire(wire.Managed));
    }

    private static ManagedObjectRefDto FromWire(ManagedObjectWire wire)
    {
        return new ManagedObjectRefDto(
            FromWire(wire.Handle),
            wire.TypeIdentity is null ? null : FromWire(wire.TypeIdentity),
            wire.BoundaryId,
            wire.ContextId);
    }

    private static TypeIdentityDto FromWire(TypeIdentityWire wire)
    {
        return new TypeIdentityDto(
            wire.TypeId,
            wire.FullName,
            wire.AssemblySimpleName,
            wire.BoundaryId,
            wire.IsValueType,
            wire.GenericArguments.Select(FromWire),
            wire.Interfaces.Select(FromWire),
            wire.AssemblyVersion,
            wire.AssemblyCulture,
            wire.PublicKeyToken,
            wire.ModuleVersionId,
            wire.DeclaringType is null ? null : FromWire(wire.DeclaringType),
            wire.GenericDefinition is null ? null : FromWire(wire.GenericDefinition),
            wire.ArrayRank,
            wire.ArrayShape,
            wire.PointerElementType is null ? null : FromWire(wire.PointerElementType),
            wire.ByRefElementType is null ? null : FromWire(wire.ByRefElementType),
            wire.NullableUnderlyingType is null ? null : FromWire(wire.NullableUnderlyingType),
            wire.BaseType is null ? null : FromWire(wire.BaseType),
            wire.DynamicIdentity);
    }

    private static TypeRefDto FromWire(TypeRefWire wire)
    {
        return new TypeRefDto(wire.TypeId, wire.BoundaryId);
    }

    private static CorrelationEffectSummaryDto FromWire(EffectWire wire)
    {
        return new CorrelationEffectSummaryDto(
            wire.Categories.Select(category => (EffectCategory)category),
            (FrameworkStateEffect)wire.FrameworkState,
            (ApplicationCallbackEffect)wire.ApplicationCallbacks,
            wire.CallbackDetails.Select(detail => new CallbackDetailDto(
                detail.Name,
                detail.Count,
                detail.CountKnown)),
            (VisibleMutationEffect)wire.VisibleMutation,
            wire.Operations);
    }

    private static AdapterMetadataDto FromWire(MetadataWire wire)
    {
        return new AdapterMetadataDto(
            wire.AdapterId,
            wire.SchemaId,
            wire.SchemaVersion,
            FromWire(wire.Payload));
    }

    private static DetachedMetadataValueDto FromWire(MetadataValueWire wire)
    {
        return (DetachedMetadataValueKind)wire.Kind switch
        {
            DetachedMetadataValueKind.Null => DetachedMetadataValueDto.Null(),
            DetachedMetadataValueKind.Boolean => DetachedMetadataValueDto.Boolean(
                wire.BooleanValue ?? throw new InvalidOperationException("Boolean metadata payload was missing.")),
            DetachedMetadataValueKind.Integer => DetachedMetadataValueDto.Integer(
                wire.IntegerValue ?? throw new InvalidOperationException("Integer metadata payload was missing.")),
            DetachedMetadataValueKind.Decimal => DetachedMetadataValueDto.Decimal(
                wire.DecimalValue ?? throw new InvalidOperationException("Decimal metadata payload was missing.")),
            DetachedMetadataValueKind.FloatingPoint => DetachedMetadataValueDto.FloatingPoint(
                wire.FloatingPointValue ?? throw new InvalidOperationException("Floating-point metadata payload was missing.")),
            DetachedMetadataValueKind.String => DetachedMetadataValueDto.String(
                wire.StringValue ?? throw new InvalidOperationException("String metadata payload was missing.")),
            DetachedMetadataValueKind.Array => DetachedMetadataValueDto.Array(
                wire.ArrayValue?.Select(FromWire)
                    ?? throw new InvalidOperationException("Array metadata payload was missing.")),
            DetachedMetadataValueKind.Object => DetachedMetadataValueDto.Object(
                wire.ObjectValue?.Select(property => new DetachedMetadataPropertyDto(
                    property.Name,
                    FromWire(property.Value)))
                    ?? throw new InvalidOperationException("Object metadata payload was missing.")),
            _ => throw new InvalidOperationException($"Unknown metadata kind '{wire.Kind}'.")
        };
    }

    private static CorrelationLimitationDto FromWire(LimitationWire wire)
    {
        return new CorrelationLimitationDto(wire.Code, wire.Detail);
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
        string? Text);

    private sealed record TargetEvidenceWire(
        string AdapterId,
        int ProcessId,
        TargetWire? CandidateTarget,
        FactWire[] EvidenceFacts,
        ValidationWire[] ValidationFacts,
        EffectWire Effects,
        MetadataWire[] AdapterMetadata,
        LimitationWire[] Limitations,
        ErrorWire? Error);

    private sealed record TargetWire(int TargetKind, ManagedObjectWire? Managed);

    private sealed record ManagedObjectWire(
        HandleWire Handle,
        TypeIdentityWire? TypeIdentity,
        string? BoundaryId,
        string? ContextId);

    private sealed record TypeIdentityWire(
        string TypeId,
        string FullName,
        string AssemblySimpleName,
        string? AssemblyVersion,
        string? AssemblyCulture,
        string? PublicKeyToken,
        string? ModuleVersionId,
        string BoundaryId,
        TypeRefWire? DeclaringType,
        TypeRefWire? GenericDefinition,
        TypeRefWire[] GenericArguments,
        int? ArrayRank,
        int[]? ArrayShape,
        TypeRefWire? PointerElementType,
        TypeRefWire? ByRefElementType,
        TypeRefWire? NullableUnderlyingType,
        bool IsValueType,
        TypeRefWire? BaseType,
        TypeRefWire[] Interfaces,
        string? DynamicIdentity);

    private sealed record TypeRefWire(string TypeId, string BoundaryId);

    private sealed record EffectWire(
        int[] Categories,
        int FrameworkState,
        int ApplicationCallbacks,
        CallbackWire[] CallbackDetails,
        int VisibleMutation,
        string[] Operations);

    private sealed record CallbackWire(string Name, int? Count, bool CountKnown);

    private sealed record MetadataWire(
        string AdapterId,
        string SchemaId,
        int SchemaVersion,
        MetadataValueWire Payload);

    private sealed record MetadataValueWire(
        int Kind,
        bool? BooleanValue,
        long? IntegerValue,
        decimal? DecimalValue,
        double? FloatingPointValue,
        string? StringValue,
        MetadataValueWire[]? ArrayValue,
        MetadataPropertyWire[]? ObjectValue);

    private sealed record MetadataPropertyWire(string Name, MetadataValueWire Value);

    private sealed record LimitationWire(string Code, string? Detail);

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
