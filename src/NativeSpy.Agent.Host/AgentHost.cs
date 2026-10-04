using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using NativeSpy.Agent;
using NativeSpy.Protocol.Common;
using NativeSpy.Protocol.Json;
using NativeSpy.Transport.NamedPipes;

namespace NativeSpy.Agent.Host;

public sealed class AgentHost : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly AgentHostOptions _options;
    private readonly IAgentHostCompositionFactory _compositionFactory;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly ConcurrentDictionary<ulong, Task> _inflight = new();
    private readonly TaskCompletionSource<object?> _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private AgentHostState _state = AgentHostState.Created;
    private NamedPipeServer? _server;
    private NamedPipeConnection? _connection;
    private ClrAgentSession? _session;
    private IAgentHostComposition? _composition;
    private IAgentOperationHandler[] _handlers = Array.Empty<IAgentOperationHandler>();
    private string[] _adapterIds = Array.Empty<string>();
    private SemaphoreSlim? _requestSlots;
    private Task? _acceptLoop;
    private Task? _bootstrapExpiry;
    private int _selectedProtocolVersion;
    private string? _activeSessionId;
    private ulong _lastRequestId;
    private bool _nonceValid = true;
    private int _stopped;

    public AgentHost(
        AgentHostOptions options,
        IAgentHostCompositionFactory compositionFactory)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _compositionFactory = compositionFactory ?? throw new ArgumentNullException(nameof(compositionFactory));
        BootstrapDescriptor = _options.CreateBootstrapDescriptor();
    }

    public AgentHostState State
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    public BootstrapDescriptorWire BootstrapDescriptor { get; }

    public string? SessionId
    {
        get
        {
            lock (_gate)
            {
                return _session?.SessionId ?? _activeSessionId;
            }
        }
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_state != AgentHostState.Created)
            {
                throw new InvalidOperationException("The Agent Host has already been started.");
            }

            _server = new NamedPipeServer(new NamedPipeServerOptions(
                _options.PipeName,
                _options.AllowedUserSid,
                _options.MaximumFrameBytes));
            _requestSlots = new SemaphoreSlim(_options.MaximumOutstandingRequests);
            _state = AgentHostState.Listening;
            _acceptLoop = AcceptLoopAsync(_shutdown.Token);
            _bootstrapExpiry = ExpireBootstrapAsync(_shutdown.Token);
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        NamedPipeConnection? connection;
        NamedPipeServer? server;
        ClrAgentSession? session;
        IAgentHostComposition? composition;
        lock (_gate)
        {
            if (_state == AgentHostState.Closed)
            {
                return;
            }

            _state = AgentHostState.Closing;
            _nonceValid = false;
            connection = _connection;
            _connection = null;
            server = _server;
            _server = null;
            session = _session;
            _session = null;
            composition = _composition;
            _composition = null;
            _handlers = Array.Empty<IAgentOperationHandler>();
            _adapterIds = Array.Empty<string>();
            _requestSlots = null;
            _shutdown.Cancel();
        }

        connection?.Abort();
        session?.Close();

        if (composition is not null)
        {
            try
            {
                await composition.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
                // Shutdown must not hide the terminal session state.
            }
        }

        if (server is not null)
        {
            try
            {
                await server.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
                // Shutdown must remain terminal even if the listener is already closed.
            }
        }

        _options.Dispose();
        if (Interlocked.Exchange(ref _stopped, 1) == 0)
        {
            lock (_gate)
            {
                _state = AgentHostState.Closed;
            }

            _closed.TrySetResult(null);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        var inflight = _inflight.Values.ToArray();
        if (inflight.Length > 0)
        {
            try
            {
                await Task.WhenAll(inflight).ConfigureAwait(false);
            }
            catch
            {
                // The terminal Host state is already published.
            }
        }

        _shutdown.Dispose();
        _closed.TrySetCanceled();
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            NamedPipeConnection? candidate = null;
            try
            {
                lock (_gate)
                {
                    if (_state != AgentHostState.Listening)
                    {
                        return;
                    }

                    _state = AgentHostState.Authenticating;
                }

                var server = _server;
                if (server is null)
                {
                    return;
                }

                candidate = await server.AcceptAsync(cancellationToken).ConfigureAwait(false);
                using var handshakeCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                handshakeCancellation.CancelAfter(_options.HandshakeTimeout);
                var activated = await AuthenticateAndActivateAsync(
                        candidate,
                        handshakeCancellation.Token)
                    .ConfigureAwait(false);
                if (!activated)
                {
                    await candidate.DisposeAsync().ConfigureAwait(false);
                    candidate = null;
                    lock (_gate)
                    {
                        if (_state == AgentHostState.Authenticating)
                        {
                            _state = AgentHostState.Listening;
                        }
                    }

                    continue;
                }

                candidate = null;
                NamedPipeConnection? activeConnection;
                lock (_gate)
                {
                    activeConnection = _connection;
                }

                if (activeConnection is not null)
                {
                    await RunConnectionAsync(activeConnection, cancellationToken).ConfigureAwait(false);
                }

                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (OperationCanceledException)
            {
                candidate?.Abort();
                lock (_gate)
                {
                    if (_state == AgentHostState.Authenticating)
                    {
                        _state = AgentHostState.Listening;
                    }
                }
            }
            catch (Exception exception)
            {
                WriteDiagnostic("The Agent Host listener failed.", exception);
                candidate?.Abort();
                await StopAsync().ConfigureAwait(false);
                return;
            }
            finally
            {
                if (candidate is not null)
                {
                    candidate.Abort();
                }
            }
        }
    }

    private async Task<bool> AuthenticateAndActivateAsync(
        NamedPipeConnection connection,
        CancellationToken cancellationToken)
    {
        NamedPipeFrame? frame = null;
        try
        {
            frame = await connection.ReadFrameAsync(cancellationToken).ConfigureAwait(false);
            if (frame is null)
            {
                return false;
            }

            HelloRequestWire hello;
            try
            {
                hello = ProtocolJsonCodec.DeserializeHelloRequest(frame.Payload);
                hello.EnsureMessageKind();
            }
            catch (ProtocolJsonException exception)
            {
                await SendHandshakeErrorAsync(
                        connection,
                        new ProtocolErrorDto(ProtocolErrorCode.InvalidRequest, "The hello request is invalid."),
                        cancellationToken)
                    .ConfigureAwait(false);
                WriteDiagnostic("The Agent Host rejected an invalid hello request.", exception);
                return false;
            }

            if (hello.MinSupportedVersion > hello.MaxSupportedVersion)
            {
                await SendHandshakeErrorAsync(
                        connection,
                        new ProtocolErrorDto(ProtocolErrorCode.InvalidRequest, "The protocol range is invalid."),
                        cancellationToken)
                    .ConfigureAwait(false);
                return false;
            }

            var selectedVersion = Math.Min(_options.MaxSupportedVersion, hello.MaxSupportedVersion);
            if (selectedVersion < Math.Max(_options.MinSupportedVersion, hello.MinSupportedVersion))
            {
                await SendHandshakeErrorAsync(
                        connection,
                        new ProtocolErrorDto(ProtocolErrorCode.ProtocolMismatch),
                        cancellationToken)
                    .ConfigureAwait(false);
                return false;
            }

            if (!ProcessIdentityMatches(hello.ExpectedTargetProcessIdentity)
                || !NonceMatches(hello.BootstrapNonce)
                || !PeerSidMatches(connection))
            {
                await SendHandshakeErrorAsync(
                        connection,
                        new ProtocolErrorDto(ProtocolErrorCode.AuthenticationFailed),
                        cancellationToken)
                    .ConfigureAwait(false);
                return false;
            }

            ClrAgentSession? session = null;
            IAgentHostComposition? composition = null;
            try
            {
                session = new ClrAgentSession();
                var context = new HostSessionContext(
                    session.SessionId,
                    selectedVersion,
                    _options.TargetProcessIdentity);
                composition = _compositionFactory.Create(session, context)
                    ?? throw new InvalidOperationException("The Host composition factory returned null.");
                var handlers = FreezeComposition(composition);
                var adapterIds = composition is IAgentHostAdapterMetadata metadata
                    ? metadata.AdapterIds.ToArray()
                    : Array.Empty<string>();

                lock (_gate)
                {
                    if (_state != AgentHostState.Authenticating)
                    {
                        throw new OperationCanceledException("The Host stopped during activation.");
                    }

                    _state = AgentHostState.Activating;
                    _selectedProtocolVersion = selectedVersion;
                    _activeSessionId = session.SessionId;
                    _session = session;
                    _composition = composition;
                    _handlers = handlers;
                    _adapterIds = adapterIds;
                    _connection = connection;
                    _nonceValid = false;
                    session = null;
                    composition = null;
                    _state = AgentHostState.Active;
                }

                var response = new HelloResponseWire
                {
                    SelectedProtocolVersion = selectedVersion,
                    SessionId = SessionId!,
                    TargetProcessIdentity = new ProcessIdentityWire
                    {
                        ProcessId = _options.TargetProcessIdentity.ProcessId,
                        ProcessStartIdentity = _options.TargetProcessIdentity.ProcessStartIdentity
                    },
                    Capabilities = new CapabilitiesWire
                    {
                        SupportedOperations = GetSupportedOperations(),
                        AdapterIds = GetAdapterIds(),
                        SingleClient = true,
                        ReconnectSupported = false
                    },
                    Limits = new LimitsWire
                    {
                        MaxFrameBytes = _options.MaximumFrameBytes,
                        MaxJsonDepth = _options.MaximumJsonDepth,
                        DefaultBudgetMs = _options.DefaultBudgetMs,
                        MaxBudgetMs = _options.MaxBudgetMs,
                        MaxOutstandingRequests = _options.MaximumOutstandingRequests
                    }
                };
                var responseBytes = ProtocolJsonCodec.SerializeHelloResponse(response);
                if (responseBytes.Length > _options.MaximumFrameBytes)
                {
                    throw new InvalidOperationException("The hello response exceeded the configured frame limit.");
                }

                try
                {
                    await connection.WriteFrameAsync(responseBytes, cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    await StopAsync().ConfigureAwait(false);
                    return false;
                }

                return true;
            }
            catch (OperationCanceledException)
            {
                if (session is not null)
                {
                    session.Close();
                }

                if (composition is not null)
                {
                    await composition.DisposeAsync().ConfigureAwait(false);
                }

                await StopAsync().ConfigureAwait(false);
                return false;
            }
            catch (Exception exception)
            {
                WriteDiagnostic("The Agent Host composition failed.", exception);
                if (session is not null)
                {
                    session.Close();
                }

                if (composition is not null)
                {
                    await composition.DisposeAsync().ConfigureAwait(false);
                }

                try
                {
                    await SendHandshakeErrorAsync(
                            connection,
                            new ProtocolErrorDto(ProtocolErrorCode.InternalFailure),
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                catch
                {
                    // The candidate may already have disconnected.
                }

                await StopAsync().ConfigureAwait(false);
                return false;
            }
        }
        finally
        {
            frame?.Dispose();
        }
    }

    private async Task RunConnectionAsync(
        NamedPipeConnection connection,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var slots = _requestSlots;
            if (slots is null)
            {
                return;
            }

            try
            {
                await slots.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            NamedPipeFrame? frame = null;
            try
            {
                frame = await connection.ReadFrameAsync(cancellationToken).ConfigureAwait(false);
                if (frame is null)
                {
                    slots.Release();
                    await StopAsync().ConfigureAwait(false);
                    return;
                }

                RequestEnvelopeWire request;
                try
                {
                    request = ProtocolJsonCodec.DeserializeRequest(frame.Payload);
                    request.EnsureMessageKind();
                }
                catch (ProtocolJsonException exception)
                {
                    WriteDiagnostic("The Agent Host received an invalid request envelope.", exception);
                    if (ProtocolJsonCodec.TryReadRequestId(frame.Payload, out var malformedRequestId)
                        && malformedRequestId is not null
                        && TryParseRequestId(malformedRequestId, out var malformedRequestNumber)
                        && malformedRequestNumber > _lastRequestId)
                    {
                        _lastRequestId = malformedRequestNumber;
                        await SendProtocolErrorAsync(
                                connection,
                                malformedRequestId,
                                ProtocolErrorCode.InvalidRequest)
                            .ConfigureAwait(false);
                        slots.Release();
                        continue;
                    }

                    slots.Release();
                    await StopForProtocolFailureAsync(connection, ProtocolErrorCode.InvalidRequest).ConfigureAwait(false);
                    return;
                }

                if (!TryParseRequestId(request.RequestId, out var requestNumber))
                {
                    slots.Release();
                    await StopForProtocolFailureAsync(connection, ProtocolErrorCode.ProtocolViolation).ConfigureAwait(false);
                    return;
                }

                if (requestNumber <= _lastRequestId)
                {
                    slots.Release();
                    await SendProtocolErrorAndStopAsync(
                            connection,
                            request.RequestId,
                            ProtocolErrorCode.ProtocolViolation)
                        .ConfigureAwait(false);
                    return;
                }

                _lastRequestId = requestNumber;
                if (!string.Equals(request.SessionId, SessionId, StringComparison.Ordinal)
                    || request.ProtocolVersion != _selectedProtocolVersion)
                {
                    slots.Release();
                    await SendProtocolErrorAndStopAsync(
                            connection,
                            request.RequestId,
                            ProtocolErrorCode.ProtocolViolation)
                        .ConfigureAwait(false);
                    return;
                }

                if (request.BudgetMs is 0
                    || request.BudgetMs is ulong requestedBudget && requestedBudget > _options.MaxBudgetMs)
                {
                    await SendProtocolErrorAsync(
                            connection,
                            request.RequestId,
                            ProtocolErrorCode.InvalidRequest)
                        .ConfigureAwait(false);
                    slots.Release();
                    continue;
                }

                var handler = FindHandler(request.Operation);
                if (handler is null)
                {
                    await SendProtocolErrorAsync(
                            connection,
                            request.RequestId,
                            ProtocolErrorCode.UnsupportedOperation)
                        .ConfigureAwait(false);
                    slots.Release();
                    continue;
                }

                var budget = request.BudgetMs ?? _options.DefaultBudgetMs;
                var task = ProcessRequestAsync(
                    connection,
                    request,
                    requestNumber,
                    budget,
                    handler,
                    slots);
                _inflight[requestNumber] = task;
                _ = task.ContinueWith(
                    _ => RemoveInflight(requestNumber),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
                frame = null;
            }
            catch (NamedPipeFrameException exception)
            {
                slots.Release();
                WriteDiagnostic("The Agent Host received an invalid or truncated frame.", exception);
                await StopAsync().ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                slots.Release();
                return;
            }
            catch (Exception exception)
            {
                slots.Release();
                WriteDiagnostic("The Agent Host connection loop failed.", exception);
                await StopAsync().ConfigureAwait(false);
                return;
            }
            finally
            {
                frame?.Dispose();
            }
        }
    }

    private async Task ProcessRequestAsync(
        NamedPipeConnection connection,
        RequestEnvelopeWire request,
        ulong requestNumber,
        ulong budgetMs,
        IAgentOperationHandler handler,
        SemaphoreSlim slots)
    {
        using var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
        var context = new AgentRequestContext(
            requestNumber,
            request.RequestId,
            request.SessionId,
            request.ProtocolVersion,
            budgetMs);
        var handlerTask = Task.Run(
            () => handler.HandleAsync(context, request.Payload, requestCancellation.Token),
            CancellationToken.None);
        var timeoutTask = Task.Delay(context.Remaining);

        try
        {
            var winner = await Task.WhenAny(handlerTask, timeoutTask).ConfigureAwait(false);
            if (winner == timeoutTask && !handlerTask.IsCompleted)
            {
                requestCancellation.Cancel();
                await SendOperationErrorAsync(
                        connection,
                        request.RequestId,
                        new OperationErrorDto(OperationErrorCode.TargetTimeout, "The operation budget expired."))
                    .ConfigureAwait(false);
                _ = ObserveLateHandlerAsync(handlerTask);
                return;
            }

            AgentHandlerResult result;
            try
            {
                result = await handlerTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
            {
                return;
            }
            catch (AgentOperationDispatchException exception)
            {
                await SendOperationErrorAsync(connection, request.RequestId, exception.Error).ConfigureAwait(false);
                return;
            }
            catch (ProtocolJsonException exception)
            {
                WriteDiagnostic("The operation payload was invalid.", exception);
                await SendProtocolErrorAsync(
                        connection,
                        request.RequestId,
                        ProtocolErrorCode.InvalidRequest)
                    .ConfigureAwait(false);
                return;
            }
            catch (Exception exception)
            {
                WriteDiagnostic("An operation handler failed unexpectedly.", exception);
                await SendProtocolErrorAsync(
                        connection,
                        request.RequestId,
                        ProtocolErrorCode.InternalFailure)
                    .ConfigureAwait(false);
                await StopAsync().ConfigureAwait(false);
                return;
            }

            if (context.IsExpired)
            {
                await SendOperationErrorAsync(
                        connection,
                        request.RequestId,
                        new OperationErrorDto(OperationErrorCode.TargetTimeout, "The operation budget expired."))
                    .ConfigureAwait(false);
                return;
            }

            ResponseEnvelopeWire response;
            if (result.OperationError is not null)
            {
                response = CreateOperationErrorResponse(request.RequestId, result.OperationError);
            }
            else if (result.Payload is JsonElement payload)
            {
                response = new ResponseEnvelopeWire
                {
                    ProtocolVersion = _selectedProtocolVersion,
                    SessionId = SessionId!,
                    RequestId = request.RequestId,
                    ResultStatus = ProtocolJsonCodec.SuccessStatus,
                    Payload = payload.Clone()
                };
            }
            else
            {
                await SendProtocolErrorAsync(
                        connection,
                        request.RequestId,
                        ProtocolErrorCode.InternalFailure)
                    .ConfigureAwait(false);
                await StopAsync().ConfigureAwait(false);
                return;
            }

            await SendCommittedResponseAsync(connection, response).ConfigureAwait(false);
        }
        finally
        {
            slots.Release();
        }
    }

    private void RemoveInflight(ulong requestNumber)
    {
        _inflight.TryRemove(requestNumber, out var ignored);
    }

    private async Task ObserveLateHandlerAsync(Task<AgentHandlerResult> handlerTask)
    {
        try
        {
            await handlerTask.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            WriteDiagnostic("A timed-out operation completed with an exception.", exception);
        }
    }

    private async Task SendOperationErrorAsync(
        NamedPipeConnection connection,
        string requestId,
        OperationErrorDto error)
    {
        await SendCommittedResponseAsync(
                connection,
                CreateOperationErrorResponse(requestId, error))
            .ConfigureAwait(false);
    }

    private async Task SendProtocolErrorAsync(
        NamedPipeConnection connection,
        string requestId,
        ProtocolErrorCode code)
    {
        var response = new ResponseEnvelopeWire
        {
            ProtocolVersion = _selectedProtocolVersion,
            SessionId = SessionId!,
            RequestId = requestId,
            ResultStatus = ProtocolJsonCodec.ProtocolErrorStatus,
            ProtocolError = new ProtocolErrorWire { Code = Enum.GetName(typeof(ProtocolErrorCode), code)! }
        };
        await SendCommittedResponseAsync(connection, response).ConfigureAwait(false);
    }

    private async Task SendProtocolErrorAndStopAsync(
        NamedPipeConnection connection,
        string requestId,
        ProtocolErrorCode code)
    {
        try
        {
            await SendProtocolErrorAsync(connection, requestId, code).ConfigureAwait(false);
        }
        finally
        {
            await StopAsync().ConfigureAwait(false);
        }
    }

    private async Task SendCommittedResponseAsync(
        NamedPipeConnection connection,
        ResponseEnvelopeWire response)
    {
        byte[] bytes;
        try
        {
            bytes = ProtocolJsonCodec.SerializeResponse(response);
        }
        catch (ProtocolJsonException exception)
        {
            WriteDiagnostic("The Host response could not be serialized.", exception);
            bytes = ProtocolJsonCodec.SerializeResponse(new ResponseEnvelopeWire
            {
                ProtocolVersion = _selectedProtocolVersion,
                SessionId = SessionId!,
                RequestId = response.RequestId,
                ResultStatus = ProtocolJsonCodec.ProtocolErrorStatus,
                ProtocolError = new ProtocolErrorWire
                {
                    Code = nameof(ProtocolErrorCode.InternalFailure)
                }
            });
        }

        if (bytes.Length > _options.MaximumFrameBytes)
        {
            bytes = ProtocolJsonCodec.SerializeResponse(new ResponseEnvelopeWire
            {
                ProtocolVersion = _selectedProtocolVersion,
                SessionId = SessionId!,
                RequestId = response.RequestId,
                ResultStatus = ProtocolJsonCodec.ProtocolErrorStatus,
                ProtocolError = new ProtocolErrorWire
                {
                    Code = nameof(ProtocolErrorCode.ResponseTooLarge)
                }
            });
        }

        if (bytes.Length > _options.MaximumFrameBytes)
        {
            await StopAsync().ConfigureAwait(false);
            return;
        }

        try
        {
            await connection.WriteFrameAsync(bytes, _shutdown.Token).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            WriteDiagnostic("The Agent Host could not write a response.", exception);
            await StopAsync().ConfigureAwait(false);
        }
    }

    private async Task SendHandshakeErrorAsync(
        NamedPipeConnection connection,
        ProtocolErrorDto error,
        CancellationToken cancellationToken)
    {
        var bytes = ProtocolJsonCodec.SerializeHandshakeError(error);
        if (bytes.Length <= _options.MaximumFrameBytes)
        {
            try
            {
                await connection.WriteFrameAsync(bytes, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // The unauthenticated candidate may already have disconnected.
            }
        }
    }

    private IAgentOperationHandler? FindHandler(string operation)
    {
        return _handlers.FirstOrDefault(handler =>
            string.Equals(handler.OperationName, operation, StringComparison.Ordinal));
    }

    private string[] GetSupportedOperations()
    {
        return _handlers
            .Select(handler => handler.OperationName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
    }

    private string[] GetAdapterIds()
    {
        return _adapterIds.ToArray();
    }

    private static IAgentOperationHandler[] FreezeComposition(IAgentHostComposition composition)
    {
        var handlers = composition.Handlers.ToArray();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var handler in handlers)
        {
            if (handler is null || string.IsNullOrWhiteSpace(handler.OperationName))
            {
                throw new InvalidOperationException("A Host operation handler must have a name.");
            }

            if (!names.Add(handler.OperationName))
            {
                throw new InvalidOperationException(
                    $"The Host operation '{handler.OperationName}' was registered more than once.");
            }
        }

        return handlers;
    }

    private bool ProcessIdentityMatches(ProcessIdentityWire identity)
    {
        return identity.ProcessId == _options.TargetProcessIdentity.ProcessId
            && string.Equals(
                identity.ProcessStartIdentity,
                _options.TargetProcessIdentity.ProcessStartIdentity,
                StringComparison.Ordinal);
    }

    private bool NonceMatches(string encodedNonce)
    {
        if (!_nonceValid)
        {
            return false;
        }

        byte[]? decoded = null;
        try
        {
            decoded = DecodeBase64Url(encodedNonce);
            return CryptographicOperations.FixedTimeEquals(decoded, _options.BootstrapNonceBytes);
        }
        catch (FormatException)
        {
            return false;
        }
        finally
        {
            if (decoded is not null)
            {
                CryptographicOperations.ZeroMemory(decoded);
            }
        }
    }

    private bool PeerSidMatches(NamedPipeConnection connection)
    {
        return connection.TryGetConnectedUserSid(out var sid)
            && sid is not null
            && sid.Equals(_options.AllowedUserSid);
    }

    private async Task ExpireBootstrapAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(_options.BootstrapLifetime, cancellationToken).ConfigureAwait(false);
            lock (_gate)
            {
                if (_state is AgentHostState.Listening or AgentHostState.Authenticating)
                {
                    _nonceValid = false;
                }
                else
                {
                    return;
                }
            }

            await StopAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private ResponseEnvelopeWire CreateOperationErrorResponse(
        string requestId,
        OperationErrorDto error)
    {
        return new ResponseEnvelopeWire
        {
            ProtocolVersion = _selectedProtocolVersion,
            SessionId = SessionId!,
            RequestId = requestId,
            ResultStatus = ProtocolJsonCodec.OperationErrorStatus,
            OperationError = new OperationErrorWire
            {
                Code = Enum.GetName(typeof(OperationErrorCode), error.Code)!,
                Message = error.Message
            }
        };
    }

    private static bool TryParseRequestId(string value, out ulong requestId)
    {
        requestId = 0;
        if (string.IsNullOrWhiteSpace(value)
            || !ulong.TryParse(
                value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out requestId)
            || requestId == 0
            || !string.Equals(
                requestId.ToString(CultureInfo.InvariantCulture),
                value,
                StringComparison.Ordinal))
        {
            requestId = 0;
            return false;
        }

        return true;
    }

    private static byte[] DecodeBase64Url(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new FormatException("The nonce is empty.");
        }

        var normalized = value.Replace('-', '+').Replace('_', '/');
        normalized = normalized.PadRight(normalized.Length + ((4 - normalized.Length % 4) % 4), '=');
        return Convert.FromBase64String(normalized);
    }

    private async Task StopForProtocolFailureAsync(
        NamedPipeConnection connection,
        ProtocolErrorCode code)
    {
        connection.Abort();
        await StopAsync().ConfigureAwait(false);
    }

    private static void WriteDiagnostic(string message, Exception? exception = null)
    {
        try
        {
            Console.Error.WriteLine(exception is null ? message : $"{message} {exception}");
        }
        catch
        {
            // Diagnostics must never change lifecycle behavior.
        }
    }
}

public interface IAgentHostAdapterMetadata
{
    IReadOnlyList<string> AdapterIds { get; }
}
