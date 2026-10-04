using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using NativeSpy.Client.Correlation;
using NativeSpy.Protocol.Common;
using NativeSpy.Protocol.Correlation;
using NativeSpy.Protocol.Json;
using NativeSpy.Transport.NamedPipes;

namespace NativeSpy.Client.NamedPipes;

public sealed class NamedPipeClientSessionOptions
{
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(10);

    public ulong? DefaultBudgetMs { get; init; }

    public int MaximumFrameBytes { get; init; } = ProtocolWireConstants.AbsoluteMaximumFrameBytes;
}

public sealed class NamedPipeSessionException : IOException
{
    public NamedPipeSessionException(OperationErrorCode code, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
    }

    public OperationErrorCode Code { get; }
}

public sealed class NamedPipeOperationException : Exception
{
    public NamedPipeOperationException(OperationErrorDto error)
        : base(error?.Message)
    {
        Error = error ?? throw new ArgumentNullException(nameof(error));
    }

    public OperationErrorDto Error { get; }
}

public sealed class NamedPipeProtocolException : Exception
{
    public NamedPipeProtocolException(ProtocolErrorDto error)
        : base(error?.Message)
    {
        Error = error ?? throw new ArgumentNullException(nameof(error));
    }

    public ProtocolErrorDto Error { get; }
}

public sealed class NamedPipeClientSession : IWinFormsCorrelationPort, IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly NamedPipeConnection _connection;
    private readonly ProcessIdentityDto _targetProcessIdentity;
    private readonly HelloResponseWire _helloResponse;
    private readonly ulong? _configuredDefaultBudgetMs;
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private readonly SemaphoreSlim _outstandingSlots;
    private readonly ConcurrentDictionary<ulong, TaskCompletionSource<ResponseEnvelopeWire>> _pending = new();
    private readonly ConcurrentDictionary<ulong, byte> _abandoned = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Task _readerLoop;
    private Exception? _terminalError;
    private int _closed;
    private ulong _nextRequestId;

    private NamedPipeClientSession(
        NamedPipeConnection connection,
        ProcessIdentityDto targetProcessIdentity,
        HelloResponseWire helloResponse,
        ulong? configuredDefaultBudgetMs)
    {
        _connection = connection;
        _targetProcessIdentity = targetProcessIdentity;
        _helloResponse = helloResponse;
        _configuredDefaultBudgetMs = configuredDefaultBudgetMs;
        _outstandingSlots = new SemaphoreSlim(helloResponse.Limits.MaxOutstandingRequests);
        _readerLoop = ReadResponsesAsync();
    }

    public int ProtocolVersion => _helloResponse.SelectedProtocolVersion;

    public string SessionId => _helloResponse.SessionId;

    public ProcessIdentityDto TargetProcessIdentity => _targetProcessIdentity;

    public IReadOnlyList<string> SupportedOperations => _helloResponse.Capabilities.SupportedOperations;

    public HelloResponseWire HelloResponse => _helloResponse;

    public static async Task<NamedPipeClientSession> ConnectAsync(
        BootstrapDescriptorWire bootstrap,
        ProcessIdentityDto expectedTargetProcessIdentity,
        NamedPipeClientSessionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (bootstrap is null)
        {
            throw new ArgumentNullException(nameof(bootstrap));
        }

        if (expectedTargetProcessIdentity is null)
        {
            throw new ArgumentNullException(nameof(expectedTargetProcessIdentity));
        }

        bootstrap.EnsureMessageKind();
        if (!ProcessIdentityMatches(bootstrap.TargetProcessIdentity, expectedTargetProcessIdentity))
        {
            throw new NamedPipeSessionException(
                OperationErrorCode.TargetExited,
                "The bootstrap descriptor does not identify the expected target process instance.");
        }

        options ??= new NamedPipeClientSessionOptions();
        if (options.ConnectTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options.ConnectTimeout));
        }

        var maximumFrameBytes = options.MaximumFrameBytes;
        if (maximumFrameBytes <= 0 || maximumFrameBytes > ProtocolWireConstants.AbsoluteMaximumFrameBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(options.MaximumFrameBytes));
        }

        NamedPipeConnection? connection = null;
        try
        {
            connection = await NamedPipeClient.ConnectAsync(
                    new NamedPipeClientOptions(bootstrap.PipeName, maximumFrameBytes),
                    options.ConnectTimeout,
                    cancellationToken)
                .ConfigureAwait(false);
            var hello = new HelloRequestWire
            {
                MinSupportedVersion = bootstrap.MinSupportedVersion,
                MaxSupportedVersion = bootstrap.MaxSupportedVersion,
                ExpectedTargetProcessIdentity = new ProcessIdentityWire
                {
                    ProcessId = expectedTargetProcessIdentity.ProcessId,
                    ProcessStartIdentity = expectedTargetProcessIdentity.ProcessStartIdentity
                },
                BootstrapNonce = bootstrap.BootstrapNonce
            };
            var helloBytes = ProtocolJsonCodec.SerializeHelloRequest(hello);
            await connection.WriteFrameAsync(helloBytes, cancellationToken).ConfigureAwait(false);
            using var responseFrame = await connection.ReadFrameAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new NamedPipeSessionException(
                    OperationErrorCode.SessionClosed,
                    "The named pipe closed during authentication.");

            HelloResponseWire helloResponse;
            try
            {
                helloResponse = ProtocolJsonCodec.DeserializeHelloResponse(responseFrame.Payload);
                helloResponse.EnsureMessageKind();
            }
            catch (ProtocolJsonException responseException)
            {
                try
                {
                    var error = ProtocolJsonCodec.DeserializeHandshakeError(responseFrame.Payload);
                    throw new NamedPipeProtocolException(error);
                }
                catch (NamedPipeProtocolException)
                {
                    throw;
                }
                catch (ProtocolJsonException)
                {
                    throw new NamedPipeProtocolException(
                        new ProtocolErrorDto(
                            ProtocolErrorCode.InvalidRequest,
                            "The Host returned an invalid handshake response.",
                            responseException.GetType().Name));
                }
            }

            ValidateHelloResponse(
                helloResponse,
                bootstrap,
                expectedTargetProcessIdentity,
                maximumFrameBytes);
            if (options.DefaultBudgetMs is 0
                || options.DefaultBudgetMs is ulong configuredBudget
                    && configuredBudget > helloResponse.Limits.MaxBudgetMs)
            {
                throw new ArgumentOutOfRangeException(nameof(options.DefaultBudgetMs));
            }

            var session = new NamedPipeClientSession(
                connection,
                expectedTargetProcessIdentity,
                helloResponse,
                options.DefaultBudgetMs);
            connection = null;
            return session;
        }
        catch
        {
            if (connection is not null)
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }

            throw;
        }
    }

    public async Task<ResponseEnvelopeWire> SendRequestAsync(
        string operation,
        JsonElement payload,
        ulong? budgetMs = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(operation))
        {
            throw new ArgumentException("An operation name is required.", nameof(operation));
        }

        if (payload.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            throw new ArgumentException("A request payload is required.", nameof(payload));
        }

        await _outstandingSlots.WaitAsync(cancellationToken).ConfigureAwait(false);
        ulong requestNumber = 0;
        TaskCompletionSource<ResponseEnvelopeWire>? completion = null;
        var sendGateHeld = false;
        try
        {
            ThrowIfClosed();
            var effectiveBudget = budgetMs
                ?? _configuredDefaultBudgetMs
                ?? _helloResponse.Limits.DefaultBudgetMs;
            if (effectiveBudget == 0 || effectiveBudget > _helloResponse.Limits.MaxBudgetMs)
            {
                throw new ArgumentOutOfRangeException(nameof(budgetMs));
            }

            await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            sendGateHeld = true;
            try
            {
                ThrowIfClosed();
                requestNumber = checked(++_nextRequestId);
                completion = new TaskCompletionSource<ResponseEnvelopeWire>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                if (!_pending.TryAdd(requestNumber, completion))
                {
                    throw new InvalidOperationException("The request ID was already pending.");
                }

                var request = new RequestEnvelopeWire
                {
                    ProtocolVersion = ProtocolVersion,
                    SessionId = SessionId,
                    RequestId = requestNumber.ToString(CultureInfo.InvariantCulture),
                    Operation = operation,
                    BudgetMs = effectiveBudget,
                    Payload = payload.Clone()
                };
                var bytes = ProtocolJsonCodec.SerializeRequest(request);
                if (bytes.Length > _helloResponse.Limits.MaxFrameBytes)
                {
                    _pending.TryRemove(requestNumber, out _);
                    throw new NamedPipeProtocolException(
                        new ProtocolErrorDto(ProtocolErrorCode.ResponseTooLarge, "The request exceeded the negotiated frame limit."));
                }

                try
                {
                    await _connection.WriteFrameAsync(bytes, _lifetime.Token).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    _pending.TryRemove(requestNumber, out _);
                    var terminal = CreateTerminalException(exception);
                    TransitionTerminal(terminal);
                    throw terminal;
                }
            }
            catch
            {
                if (requestNumber != 0)
                {
                    _pending.TryRemove(requestNumber, out _);
                }

                throw;
            }
            finally
            {
                if (sendGateHeld)
                {
                    _sendGate.Release();
                    sendGateHeld = false;
                }
            }

            try
            {
                return await AwaitResponseAsync(
                        requestNumber,
                        completion.Task,
                        effectiveBudget,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                if (_pending.TryRemove(requestNumber, out _)
                    && completion is not null
                    && !completion.Task.IsCompleted)
                {
                    _abandoned[requestNumber] = 0;
                }
            }
        }
        finally
        {
            _outstandingSlots.Release();
        }
    }

    public async Task<FrameworkCorrelationEvidenceDto> BeginCurrentHwndAsync(
        ulong hwnd,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await SendRequestAsync(
                    NamedPipeOperationNames.BeginCurrentHwnd,
                    ProtocolJsonCodec.CreateBeginCurrentHwndPayload(hwnd),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return ReadFrameworkResponse(response);
        }
        catch (NamedPipeOperationException exception)
        {
            return CreateFailureEvidence(exception.Error, NamedPipeOperationNames.BeginCurrentHwnd);
        }
        catch (NamedPipeSessionException exception)
        {
            return CreateFailureEvidence(
                new OperationErrorDto(exception.Code, exception.Message),
                NamedPipeOperationNames.BeginCurrentHwnd);
        }
    }

    public async Task<FrameworkCorrelationEvidenceDto> RevalidateCurrentHwndAsync(
        ulong hwnd,
        HandleRefDto candidateHandle,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await SendRequestAsync(
                    NamedPipeOperationNames.RevalidateCurrentHwnd,
                    ProtocolJsonCodec.CreateRevalidateCurrentHwndPayload(hwnd, candidateHandle),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return ReadFrameworkResponse(response);
        }
        catch (NamedPipeOperationException exception)
        {
            return CreateFailureEvidence(exception.Error, NamedPipeOperationNames.RevalidateCurrentHwnd);
        }
        catch (NamedPipeSessionException exception)
        {
            return CreateFailureEvidence(
                new OperationErrorDto(exception.Code, exception.Message),
                NamedPipeOperationNames.RevalidateCurrentHwnd);
        }
    }

    public async ValueTask DisposeAsync()
    {
        TransitionTerminal(new NamedPipeSessionException(
            OperationErrorCode.SessionClosed,
            "The Client session was closed."));
        try
        {
            await _readerLoop.ConfigureAwait(false);
        }
        catch
        {
            // The terminal error is already published to pending requests.
        }

        // The synchronization primitives are intentionally left alive until all
        // in-flight request continuations have observed the terminal state.
    }

    private async Task<ResponseEnvelopeWire> AwaitResponseAsync(
        ulong requestNumber,
        Task<ResponseEnvelopeWire> responseTask,
        ulong budgetMs,
        CancellationToken cancellationToken)
    {
        var timeoutTask = Task.Delay(TimeSpan.FromMilliseconds(budgetMs));
        var winner = await Task.WhenAny(responseTask, timeoutTask).WaitAsync(cancellationToken).ConfigureAwait(false);
        if (winner == timeoutTask && !responseTask.IsCompleted)
        {
            if (_pending.TryRemove(requestNumber, out _))
            {
                _abandoned[requestNumber] = 0;
            }

            throw new NamedPipeOperationException(
                new OperationErrorDto(OperationErrorCode.TargetTimeout, "The Client wait budget expired."));
        }

        return await responseTask.ConfigureAwait(false);
    }

    private async Task ReadResponsesAsync()
    {
        try
        {
            while (!_lifetime.IsCancellationRequested)
            {
                using var frame = await _connection.ReadFrameAsync(_lifetime.Token).ConfigureAwait(false);
                if (frame is null)
                {
                    throw CreateTerminalException(null);
                }

                ResponseEnvelopeWire response;
                try
                {
                    response = ProtocolJsonCodec.DeserializeResponse(frame.Payload);
                    response.EnsureMessageKind();
                }
                catch (ProtocolJsonException exception)
                {
                    throw new NamedPipeProtocolException(
                        new ProtocolErrorDto(ProtocolErrorCode.ProtocolViolation, exception.Message));
                }

                if (response.ProtocolVersion != ProtocolVersion
                    || !string.Equals(response.SessionId, SessionId, StringComparison.Ordinal)
                    || !TryParseRequestId(response.RequestId, out var requestNumber))
                {
                    throw new NamedPipeProtocolException(
                        new ProtocolErrorDto(ProtocolErrorCode.ProtocolViolation));
                }

                if (_pending.TryGetValue(requestNumber, out var completion))
                {
                    completion.TrySetResult(response);
                }
                else if (!_abandoned.TryRemove(requestNumber, out _))
                {
                    throw new NamedPipeProtocolException(
                        new ProtocolErrorDto(ProtocolErrorCode.ProtocolViolation, "The Host returned an unknown request ID."));
                }
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            TransitionTerminal(exception is NamedPipeProtocolException
                ? exception
                : CreateTerminalException(exception));
        }
    }

    private void TransitionTerminal(Exception error)
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0)
        {
            return;
        }

        lock (_gate)
        {
            _terminalError = error;
        }

        _lifetime.Cancel();
        _connection.Abort();
        foreach (var pair in _pending)
        {
            pair.Value.TrySetException(error);
        }

        _pending.Clear();
    }

    private void ThrowIfClosed()
    {
        if (Volatile.Read(ref _closed) != 0)
        {
            var error = _terminalError;
            if (error is not null)
            {
                throw error;
            }

            throw new NamedPipeSessionException(
                OperationErrorCode.SessionClosed,
                "The Client session is closed.");
        }
    }

    private NamedPipeSessionException CreateTerminalException(Exception? cause)
    {
        var code = DetermineTerminalCode();
        return new NamedPipeSessionException(
            code,
            code == OperationErrorCode.TargetExited
                ? "The exact target process instance has exited."
                : "The Agent session closed.",
            cause);
    }

    private OperationErrorCode DetermineTerminalCode()
    {
        try
        {
            using var process = Process.GetProcessById(_targetProcessIdentity.ProcessId);
            if (process.HasExited)
            {
                return OperationErrorCode.TargetExited;
            }

            var current = ProcessIdentityReader.ReadForProcessId(_targetProcessIdentity.ProcessId);
            return ProcessIdentityMatches(current, _targetProcessIdentity)
                ? OperationErrorCode.SessionClosed
                : OperationErrorCode.TargetExited;
        }
        catch (ArgumentException)
        {
            return OperationErrorCode.TargetExited;
        }
        catch
        {
            return OperationErrorCode.SessionClosed;
        }
    }

    private static void ValidateHelloResponse(
        HelloResponseWire response,
        BootstrapDescriptorWire bootstrap,
        ProcessIdentityDto expectedTargetProcessIdentity,
        int maximumFrameBytes)
    {
        if (!ProcessIdentityMatches(response.TargetProcessIdentity, expectedTargetProcessIdentity)
            || response.SelectedProtocolVersion < bootstrap.MinSupportedVersion
            || response.SelectedProtocolVersion > bootstrap.MaxSupportedVersion
            || response.Limits.MaxFrameBytes <= 0
            || response.Limits.MaxFrameBytes > ProtocolWireConstants.DefaultMaximumFrameBytes
            || response.Limits.MaxFrameBytes > maximumFrameBytes
            || response.Limits.MaxJsonDepth <= 0
            || response.Limits.MaxJsonDepth > ProtocolWireConstants.DefaultMaximumJsonDepth
            || response.Limits.DefaultBudgetMs == 0
            || response.Limits.MaxBudgetMs < response.Limits.DefaultBudgetMs
            || response.Limits.MaxOutstandingRequests <= 0
            || response.Limits.MaxOutstandingRequests > 8)
        {
            throw new NamedPipeProtocolException(
                new ProtocolErrorDto(ProtocolErrorCode.ProtocolViolation, "The Host hello response is invalid."));
        }
    }

    private static FrameworkCorrelationEvidenceDto ReadFrameworkResponse(ResponseEnvelopeWire response)
    {
        return response.ResultStatus switch
        {
            ProtocolJsonCodec.SuccessStatus when response.Payload is JsonElement payload
                => ProtocolJsonCodec.DeserializeFrameworkEvidence(payload),
            ProtocolJsonCodec.OperationErrorStatus when response.OperationError is not null
                => throw new NamedPipeOperationException(ToOperationError(response.OperationError)),
            ProtocolJsonCodec.ProtocolErrorStatus when response.ProtocolError is not null
                => throw new NamedPipeProtocolException(ToProtocolError(response.ProtocolError)),
            _ => throw new NamedPipeProtocolException(
                new ProtocolErrorDto(ProtocolErrorCode.ProtocolViolation))
        };
    }

    private static OperationErrorDto ToOperationError(OperationErrorWire error)
    {
        if (!Enum.TryParse<OperationErrorCode>(error.Code, ignoreCase: false, out var code)
            || !Enum.IsDefined(typeof(OperationErrorCode), code))
        {
            throw new NamedPipeProtocolException(
                new ProtocolErrorDto(ProtocolErrorCode.ProtocolViolation, "The Host returned an unknown operation error."));
        }

        return new OperationErrorDto(code, error.Message);
    }

    private static ProtocolErrorDto ToProtocolError(ProtocolErrorWire error)
    {
        if (!Enum.TryParse<ProtocolErrorCode>(error.Code, ignoreCase: false, out var code)
            || !Enum.IsDefined(typeof(ProtocolErrorCode), code))
        {
            throw new NamedPipeProtocolException(
                new ProtocolErrorDto(ProtocolErrorCode.ProtocolViolation, "The Host returned an unknown protocol error."));
        }

        return new ProtocolErrorDto(code, error.Message, error.DiagnosticId);
    }

    private FrameworkCorrelationEvidenceDto CreateFailureEvidence(
        OperationErrorDto error,
        string operationName)
    {
        return new FrameworkCorrelationEvidenceDto(
            "winforms",
            _targetProcessIdentity.ProcessId,
            candidateTarget: null,
            new[]
            {
                new CorrelationEvidenceFactDto(
                    "TargetOperationAvailable",
                    ProofOutcome.NotAvailable,
                    EvidenceKind.Deterministic,
                    error.Message)
            },
            new[]
            {
                new CorrelationValidationFactDto(
                    "TargetOperationAvailable",
                    ValidationOutcome.NotAvailable,
                    error.Message)
            },
            new CorrelationEffectSummaryDto(
                new[] { EffectCategory.Passive },
                FrameworkStateEffect.None,
                ApplicationCallbackEffect.None,
                Array.Empty<CallbackDetailDto>(),
                VisibleMutationEffect.NotRequested,
                new[] { operationName }),
            Array.Empty<AdapterMetadataDto>(),
            new[] { new CorrelationLimitationDto("NamedPipeSessionUnavailable", error.Message) },
            error);
    }

    private static bool ProcessIdentityMatches(ProcessIdentityWire wire, ProcessIdentityDto expected)
    {
        return wire.ProcessId == expected.ProcessId
            && string.Equals(
                wire.ProcessStartIdentity,
                expected.ProcessStartIdentity,
                StringComparison.Ordinal);
    }

    private static bool ProcessIdentityMatches(ProcessIdentityDto current, ProcessIdentityDto expected)
    {
        return current.ProcessId == expected.ProcessId
            && string.Equals(
                current.ProcessStartIdentity,
                expected.ProcessStartIdentity,
                StringComparison.Ordinal);
    }

    private static bool TryParseRequestId(string value, out ulong requestId)
    {
        requestId = 0;
        return ulong.TryParse(
                   value,
                   NumberStyles.None,
                   CultureInfo.InvariantCulture,
                   out requestId)
            && requestId > 0
            && string.Equals(
                requestId.ToString(CultureInfo.InvariantCulture),
                value,
                StringComparison.Ordinal);
    }
}
