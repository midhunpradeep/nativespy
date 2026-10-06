using System.Text.Json;
using System.Text.Json.Serialization;
using NativeSpy.Protocol.Common;
using NativeSpy.Protocol.Correlation;

namespace NativeSpy.Protocol.Json;

public static class ProtocolJsonCodec
{
    public const string SuccessStatus = "success";
    public const string ProtocolErrorStatus = "protocolError";
    public const string OperationErrorStatus = "operationError";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = ProtocolWireConstants.DefaultMaximumJsonDepth,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false
    };

    public static byte[] SerializeBootstrap(BootstrapDescriptorWire descriptor)
    {
        if (descriptor is null)
        {
            throw new ArgumentNullException(nameof(descriptor));
        }

        return SerializeWire(descriptor);
    }

    public static BootstrapDescriptorWire DeserializeBootstrap(ReadOnlyMemory<byte> utf8)
    {
        using var document = ParseStrict(utf8, ProtocolWireConstants.MaximumBootstrapBytes);
        var root = RequireObject(document.RootElement, "bootstrap descriptor");
        EnsureOnlyProperties(root, "bootstrap descriptor", "kind", "descriptorVersion", "pipeName", "bootstrapNonce", "targetProcessIdentity", "minSupportedVersion", "maxSupportedVersion");
        return new BootstrapDescriptorWire
        {
            Kind = RequiredString(root, "kind"),
            DescriptorVersion = RequiredPositiveInt(root, "descriptorVersion"),
            PipeName = RequiredString(root, "pipeName"),
            BootstrapNonce = RequiredString(root, "bootstrapNonce"),
            TargetProcessIdentity = ReadProcessIdentity(RequiredObjectProperty(root, "targetProcessIdentity")),
            MinSupportedVersion = RequiredPositiveInt(root, "minSupportedVersion"),
            MaxSupportedVersion = RequiredPositiveInt(root, "maxSupportedVersion")
        };
    }

    public static byte[] SerializeHelloRequest(HelloRequestWire request)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        return SerializeWire(request);
    }

    public static HelloRequestWire DeserializeHelloRequest(ReadOnlyMemory<byte> utf8)
    {
        using var document = ParseStrict(utf8, ProtocolWireConstants.DefaultMaximumFrameBytes);
        var root = RequireObject(document.RootElement, "hello request");
        EnsureOnlyProperties(root, "hello request", "messageKind", "minSupportedVersion", "maxSupportedVersion", "expectedTargetProcessIdentity", "bootstrapNonce", "diagnosticClientIdentity");
        return new HelloRequestWire
        {
            MessageKind = RequiredString(root, "messageKind"),
            MinSupportedVersion = RequiredPositiveInt(root, "minSupportedVersion"),
            MaxSupportedVersion = RequiredPositiveInt(root, "maxSupportedVersion"),
            ExpectedTargetProcessIdentity = ReadProcessIdentity(
                RequiredObjectProperty(root, "expectedTargetProcessIdentity")),
            BootstrapNonce = RequiredString(root, "bootstrapNonce"),
            DiagnosticClientIdentity = OptionalString(root, "diagnosticClientIdentity")
        };
    }

    public static byte[] SerializeHelloResponse(HelloResponseWire response)
    {
        if (response is null)
        {
            throw new ArgumentNullException(nameof(response));
        }

        return SerializeWire(response);
    }

    public static byte[] SerializeHandshakeError(ProtocolErrorDto error)
    {
        if (error is null)
        {
            throw new ArgumentNullException(nameof(error));
        }

        return SerializeWire(new HandshakeErrorWire
        {
            Error = new ProtocolErrorWire
            {
                Code = EnumName(error.Code),
                Message = error.Message,
                DiagnosticId = error.DiagnosticId
            }
        });
    }

    public static HelloResponseWire DeserializeHelloResponse(ReadOnlyMemory<byte> utf8)
    {
        using var document = ParseStrict(utf8, ProtocolWireConstants.DefaultMaximumFrameBytes);
        var root = RequireObject(document.RootElement, "hello response");
        EnsureOnlyProperties(root, "hello response", "messageKind", "selectedProtocolVersion", "sessionId", "targetProcessIdentity", "capabilities", "limits");
        var capabilities = RequiredObjectProperty(root, "capabilities");
        var limits = RequiredObjectProperty(root, "limits");
        return new HelloResponseWire
        {
            MessageKind = RequiredString(root, "messageKind"),
            SelectedProtocolVersion = RequiredPositiveInt(root, "selectedProtocolVersion"),
            SessionId = RequiredString(root, "sessionId"),
            TargetProcessIdentity = ReadProcessIdentity(
                RequiredObjectProperty(root, "targetProcessIdentity")),
            Capabilities = ReadCapabilities(capabilities),
            Limits = ReadLimits(limits)
        };
    }

    public static ProtocolErrorDto DeserializeHandshakeError(ReadOnlyMemory<byte> utf8)
    {
        using var document = ParseStrict(utf8, ProtocolWireConstants.DefaultMaximumFrameBytes);
        var root = RequireObject(document.RootElement, "handshake error");
        EnsureOnlyProperties(root, "handshake error", "messageKind", "error");
        if (!string.Equals(RequiredString(root, "messageKind"), "error", StringComparison.Ordinal))
        {
            throw new ProtocolJsonException("The handshake error header is invalid.");
        }

        var error = ReadProtocolError(RequiredObjectProperty(root, "error"));
        return new ProtocolErrorDto(
            ParseEnum<ProtocolErrorCode>(error.Code, nameof(error.Code)),
            error.Message,
            error.DiagnosticId);
    }

    public static bool TryReadRequestId(ReadOnlyMemory<byte> utf8, out string? requestId)
    {
        requestId = null;
        try
        {
            using var document = ParseStrict(utf8, ProtocolWireConstants.DefaultMaximumFrameBytes);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("requestId", out var value)
                || value.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            requestId = value.GetString();
            return !string.IsNullOrWhiteSpace(requestId);
        }
        catch (ProtocolJsonException)
        {
            return false;
        }
    }

    public static byte[] SerializeRequest(RequestEnvelopeWire request)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        EnsurePayload(request.Payload, "request payload");
        return SerializeWire(request);
    }

    public static RequestEnvelopeWire DeserializeRequest(ReadOnlyMemory<byte> utf8)
    {
        using var document = ParseStrict(utf8, ProtocolWireConstants.DefaultMaximumFrameBytes);
        var root = RequireObject(document.RootElement, "request envelope");
        EnsureOnlyProperties(root, "request envelope", "messageKind", "protocolVersion", "sessionId", "requestId", "operation", "budgetMs", "payload");
        var payload = RequiredPayloadProperty(root, "payload").Clone();
        return new RequestEnvelopeWire
        {
            MessageKind = RequiredString(root, "messageKind"),
            ProtocolVersion = RequiredPositiveInt(root, "protocolVersion"),
            SessionId = RequiredString(root, "sessionId"),
            RequestId = RequiredString(root, "requestId"),
            Operation = RequiredString(root, "operation"),
            BudgetMs = OptionalUInt64(root, "budgetMs"),
            Payload = payload
        };
    }

    public static byte[] SerializeResponse(ResponseEnvelopeWire response)
    {
        if (response is null)
        {
            throw new ArgumentNullException(nameof(response));
        }

        ValidateResponseBranch(response);
        return SerializeWire(response);
    }

    public static ResponseEnvelopeWire DeserializeResponse(ReadOnlyMemory<byte> utf8)
    {
        using var document = ParseStrict(utf8, ProtocolWireConstants.DefaultMaximumFrameBytes);
        var root = RequireObject(document.RootElement, "response envelope");
        EnsureOnlyProperties(root, "response envelope", "messageKind", "protocolVersion", "sessionId", "requestId", "resultStatus", "payload", "protocolError", "operationError");
        var status = RequiredString(root, "resultStatus");
        var hasPayload = root.TryGetProperty("payload", out var payload);
        var protocolError = root.TryGetProperty("protocolError", out var protocolErrorElement)
            ? ReadProtocolError(protocolErrorElement)
            : null;
        var operationError = root.TryGetProperty("operationError", out var operationErrorElement)
            ? ReadOperationError(operationErrorElement)
            : null;
        var response = new ResponseEnvelopeWire
        {
            MessageKind = RequiredString(root, "messageKind"),
            ProtocolVersion = RequiredPositiveInt(root, "protocolVersion"),
            SessionId = RequiredString(root, "sessionId"),
            RequestId = RequiredString(root, "requestId"),
            ResultStatus = status,
            Payload = hasPayload ? payload.Clone() : null,
            ProtocolError = protocolError,
            OperationError = operationError
        };
        ValidateResponseBranch(response);
        return response;
    }

    public static JsonElement SerializeFrameworkEvidence(FrameworkCorrelationEvidenceDto evidence)
    {
        if (evidence is null)
        {
            throw new ArgumentNullException(nameof(evidence));
        }

        var wire = ToWire(evidence);
        using var document = JsonDocument.Parse(SerializeWire(wire));
        return document.RootElement.Clone();
    }

    public static FrameworkCorrelationEvidenceDto DeserializeFrameworkEvidence(JsonElement payload)
    {
        EnsurePayload(payload, "framework evidence payload");
        ValidateNoDuplicateProperties(payload);
        try
        {
            var wire = JsonSerializer.Deserialize<FrameworkEvidenceWire>(
                payload.GetRawText(),
                SerializerOptions);
            if (wire is null)
            {
                throw new ProtocolJsonException("The framework evidence payload was null.");
            }

            return FromWire(wire);
        }
        catch (ProtocolJsonException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is JsonException
                or NotSupportedException
                or ArgumentException
                or InvalidOperationException)
        {
            throw new ProtocolJsonException("The framework evidence payload is invalid.", exception);
        }
    }

    public static JsonElement SerializeProtocolError(ProtocolErrorDto error)
    {
        if (error is null)
        {
            throw new ArgumentNullException(nameof(error));
        }

        return JsonSerializer.SerializeToDocument(
            new ProtocolErrorWire
            {
                Code = EnumName(error.Code),
                Message = error.Message,
                DiagnosticId = error.DiagnosticId
            },
            SerializerOptions).RootElement.Clone();
    }

    public static JsonElement SerializeOperationError(OperationErrorDto error)
    {
        if (error is null)
        {
            throw new ArgumentNullException(nameof(error));
        }

        return JsonSerializer.SerializeToDocument(
            new OperationErrorWire
            {
                Code = EnumName(error.Code),
                Message = error.Message
            },
            SerializerOptions).RootElement.Clone();
    }

    private static byte[] SerializeWire<T>(T value)
    {
        try
        {
            return JsonSerializer.SerializeToUtf8Bytes(value, SerializerOptions);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            throw new ProtocolJsonException("The protocol value could not be serialized.", exception);
        }
    }

    public static JsonElement CreateBeginCurrentHwndPayload(ulong hwnd)
    {
        if (hwnd == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(hwnd), hwnd, "An HWND must be positive.");
        }

        return JsonSerializer.SerializeToDocument(
            new BeginCurrentHwndPayloadWire { Hwnd = hwnd },
            SerializerOptions).RootElement.Clone();
    }

    public static JsonElement CreateRevalidateCurrentHwndPayload(
        ulong hwnd,
        HandleRefDto candidateHandle)
    {
        if (hwnd == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(hwnd), hwnd, "An HWND must be positive.");
        }

        if (candidateHandle is null)
        {
            throw new ArgumentNullException(nameof(candidateHandle));
        }

        return JsonSerializer.SerializeToDocument(
            new RevalidateCurrentHwndPayloadWire
            {
                Hwnd = hwnd,
                CandidateHandle = new HandlePayloadWire
                {
                    SessionId = candidateHandle.SessionId,
                    HandleId = candidateHandle.HandleId,
                    Generation = candidateHandle.Generation,
                    Kind = EnumName(candidateHandle.Kind),
                    BoundaryId = candidateHandle.BoundaryId
                }
            },
            SerializerOptions).RootElement.Clone();
    }

    public static ulong ReadBeginCurrentHwndPayload(JsonElement payload)
    {
        EnsurePayload(payload, "begin-current-hwnd payload");
        ValidateNoDuplicateProperties(payload);
        var root = RequireObject(payload, "begin-current-hwnd payload");
        EnsureOnlyProperties(root, "begin-current-hwnd payload", "hwnd");
        var hwnd = RequiredUInt64(root, "hwnd");
        if (hwnd == 0)
        {
            throw new ProtocolJsonException("The HWND must be positive.");
        }

        return hwnd;
    }

    public static (ulong Hwnd, HandleRefDto CandidateHandle) ReadRevalidateCurrentHwndPayload(
        JsonElement payload)
    {
        EnsurePayload(payload, "revalidate-current-hwnd payload");
        ValidateNoDuplicateProperties(payload);
        var root = RequireObject(payload, "revalidate-current-hwnd payload");
        EnsureOnlyProperties(root, "revalidate-current-hwnd payload", "hwnd", "candidateHandle");
        var hwnd = RequiredUInt64(root, "hwnd");
        if (hwnd == 0)
        {
            throw new ProtocolJsonException("The HWND must be positive.");
        }

        var handleRoot = RequiredObjectProperty(root, "candidateHandle");
        EnsureOnlyProperties(handleRoot, "candidate handle", "sessionId", "handleId", "generation", "kind", "boundaryId");
        var handle = new HandleRefDto(
            RequiredString(handleRoot, "sessionId"),
            RequiredString(handleRoot, "handleId"),
            RequiredPositiveLong(handleRoot, "generation"),
            ParseEnum<HandleKind>(RequiredString(handleRoot, "kind"), "kind"),
            OptionalString(handleRoot, "boundaryId"));
        return (hwnd, handle);
    }

    private static JsonDocument ParseStrict(ReadOnlyMemory<byte> utf8, int maximumBytes)
    {
        if (utf8.Length == 0 || utf8.Length > maximumBytes)
        {
            throw new ProtocolJsonException("The JSON payload size is outside the allowed range.");
        }

        try
        {
            var document = JsonDocument.Parse(
                utf8,
                new JsonDocumentOptions
                {
                    MaxDepth = ProtocolWireConstants.DefaultMaximumJsonDepth,
                    CommentHandling = JsonCommentHandling.Disallow,
                    AllowTrailingCommas = false
                });
            ValidateNoDuplicateProperties(document.RootElement);
            return document;
        }
        catch (ProtocolJsonException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            throw new ProtocolJsonException("The JSON payload is invalid.", exception);
        }
    }

    private static void ValidateNoDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    throw new ProtocolJsonException(
                        $"The JSON object contains duplicate property '{property.Name}'.");
                }

                ValidateNoDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                ValidateNoDuplicateProperties(item);
            }
        }
    }

    private static JsonElement RequireObject(JsonElement element, string description)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new ProtocolJsonException($"The {description} must be a JSON object.");
        }

        return element;
    }

    private static void EnsureOnlyProperties(
        JsonElement root,
        string description,
        params string[] allowedNames)
    {
        var allowed = new HashSet<string>(allowedNames, StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject())
        {
            if (!allowed.Contains(property.Name))
            {
                throw new ProtocolJsonException(
                    $"The {description} contains unknown property '{property.Name}'.");
            }
        }
    }

    private static JsonElement RequiredObjectProperty(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Object)
        {
            throw new ProtocolJsonException($"Required object property '{name}' is missing or invalid.");
        }

        return value;
    }

    private static JsonElement RequiredPayloadProperty(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value)
            || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            throw new ProtocolJsonException($"Required payload property '{name}' is missing or null.");
        }

        return value;
    }

    private static string RequiredString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
        {
            throw new ProtocolJsonException($"Required string property '{name}' is missing or invalid.");
        }

        var result = value.GetString();
        if (string.IsNullOrWhiteSpace(result))
        {
            throw new ProtocolJsonException($"Required string property '{name}' is empty.");
        }

        return result!;
    }

    private static string? OptionalString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value))
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            throw new ProtocolJsonException($"Optional string property '{name}' is invalid.");
        }

        return value.GetString();
    }

    private static ulong RequiredUInt64(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value)
            || value.ValueKind != JsonValueKind.Number
            || !value.TryGetUInt64(out var result))
        {
            throw new ProtocolJsonException($"Required unsigned integer property '{name}' is invalid.");
        }

        return result;
    }

    private static long RequiredPositiveLong(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value)
            || value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt64(out var result)
            || result <= 0)
        {
            throw new ProtocolJsonException($"Required positive integer property '{name}' is invalid.");
        }

        return result;
    }

    private static int RequiredPositiveInt(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value)
            || value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt32(out var result)
            || result <= 0)
        {
            throw new ProtocolJsonException($"Required positive integer property '{name}' is invalid.");
        }

        return result;
    }

    private static ulong? OptionalUInt64(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value))
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Number
            || !value.TryGetUInt64(out var result))
        {
            throw new ProtocolJsonException($"Optional unsigned integer property '{name}' is invalid.");
        }

        return result;
    }

    private static ProcessIdentityWire ReadProcessIdentity(JsonElement element)
    {
        var root = RequireObject(element, "process identity");
        EnsureOnlyProperties(root, "process identity", "processId", "processStartIdentity");
        return new ProcessIdentityWire
        {
            ProcessId = RequiredPositiveInt(root, "processId"),
            ProcessStartIdentity = RequiredString(root, "processStartIdentity")
        };
    }

    private static CapabilitiesWire ReadCapabilities(JsonElement element)
    {
        var root = RequireObject(element, "capabilities");
        EnsureOnlyProperties(root, "capabilities", "supportedOperations", "adapterIds", "singleClient", "reconnectSupported");
        return new CapabilitiesWire
        {
            SupportedOperations = RequiredStringArray(root, "supportedOperations"),
            AdapterIds = RequiredStringArray(root, "adapterIds"),
            SingleClient = RequiredBoolean(root, "singleClient"),
            ReconnectSupported = RequiredBoolean(root, "reconnectSupported")
        };
    }

    private static LimitsWire ReadLimits(JsonElement element)
    {
        var root = RequireObject(element, "limits");
        EnsureOnlyProperties(root, "limits", "maxFrameBytes", "maxJsonDepth", "defaultBudgetMs", "maxBudgetMs", "maxOutstandingRequests");
        return new LimitsWire
        {
            MaxFrameBytes = RequiredPositiveInt(root, "maxFrameBytes"),
            MaxJsonDepth = RequiredPositiveInt(root, "maxJsonDepth"),
            DefaultBudgetMs = RequiredPositiveUInt64(root, "defaultBudgetMs"),
            MaxBudgetMs = RequiredPositiveUInt64(root, "maxBudgetMs"),
            MaxOutstandingRequests = RequiredPositiveInt(root, "maxOutstandingRequests")
        };
    }

    private static string[] RequiredStringArray(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            throw new ProtocolJsonException($"Required string array property '{name}' is invalid.");
        }

        var values = new List<string>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString()))
            {
                throw new ProtocolJsonException($"String array property '{name}' contains an invalid value.");
            }

            values.Add(item.GetString()!);
        }

        return values.ToArray();
    }

    private static bool RequiredBoolean(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value)
            || value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new ProtocolJsonException($"Required Boolean property '{name}' is invalid.");
        }

        return value.GetBoolean();
    }

    private static ulong RequiredPositiveUInt64(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value)
            || value.ValueKind != JsonValueKind.Number
            || !value.TryGetUInt64(out var result)
            || result == 0)
        {
            throw new ProtocolJsonException($"Required positive integer property '{name}' is invalid.");
        }

        return result;
    }

    private static ProtocolErrorWire ReadProtocolError(JsonElement element)
    {
        var root = RequireObject(element, "protocol error");
        EnsureOnlyProperties(root, "protocol error", "code", "message", "diagnosticId");
        return new ProtocolErrorWire
        {
            Code = RequiredString(root, "code"),
            Message = OptionalString(root, "message"),
            DiagnosticId = OptionalString(root, "diagnosticId")
        };
    }

    private static OperationErrorWire ReadOperationError(JsonElement element)
    {
        var root = RequireObject(element, "operation error");
        EnsureOnlyProperties(root, "operation error", "code", "message");
        return new OperationErrorWire
        {
            Code = RequiredString(root, "code"),
            Message = OptionalString(root, "message")
        };
    }

    private static void EnsurePayload(JsonElement payload, string description)
    {
        if (payload.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            throw new ProtocolJsonException($"The {description} must be a non-null JSON value.");
        }
    }

    private static void EnsurePayload(JsonElement? payload, string description)
    {
        if (!payload.HasValue)
        {
            throw new ProtocolJsonException($"The {description} is missing.");
        }

        EnsurePayload(payload.Value, description);
    }

    private static void ValidateResponseBranch(ResponseEnvelopeWire response)
    {
        if (!string.Equals(response.MessageKind, "response", StringComparison.Ordinal)
            || response.ProtocolVersion <= 0
            || string.IsNullOrWhiteSpace(response.SessionId)
            || string.IsNullOrWhiteSpace(response.RequestId))
        {
            throw new ProtocolJsonException("The response envelope header is invalid.");
        }

        var hasPayload = response.Payload.HasValue;
        var hasProtocolError = response.ProtocolError is not null;
        var hasOperationError = response.OperationError is not null;
        var count = (hasPayload ? 1 : 0) + (hasProtocolError ? 1 : 0) + (hasOperationError ? 1 : 0);

        switch (response.ResultStatus)
        {
            case SuccessStatus when count == 1 && hasPayload:
                EnsurePayload(response.Payload, "success response payload");
                return;
            case ProtocolErrorStatus when count == 1 && hasProtocolError:
                return;
            case OperationErrorStatus when count == 1 && hasOperationError:
                return;
            default:
                throw new ProtocolJsonException("The response envelope contains an invalid result branch.");
        }
    }

    private static string EnumName<TEnum>(TEnum value)
        where TEnum : struct
    {
        var name = Enum.GetName(typeof(TEnum), value);
        if (name is null)
        {
            throw new ProtocolJsonException($"The enum value '{value}' is not defined.");
        }

        return name;
    }

    private static TEnum ParseEnum<TEnum>(string value, string propertyName)
        where TEnum : struct
    {
        if (!Enum.TryParse<TEnum>(value, ignoreCase: false, out var result)
            || !Enum.IsDefined(typeof(TEnum), result))
        {
            throw new ProtocolJsonException($"The enum property '{propertyName}' has an unknown value.");
        }

        return result;
    }

    private static FrameworkEvidenceWire ToWire(FrameworkCorrelationEvidenceDto evidence)
    {
        return new FrameworkEvidenceWire
        {
            AdapterId = evidence.AdapterId,
            ProcessId = evidence.ProcessId,
            CandidateTarget = evidence.CandidateTarget is null ? null : ToWire(evidence.CandidateTarget),
            EvidenceFacts = evidence.EvidenceFacts.Select(fact => new EvidenceFactWire
            {
                Name = fact.Name,
                Outcome = EnumName(fact.Outcome),
                EvidenceKind = EnumName(fact.EvidenceKind),
                Detail = fact.Detail
            }).ToArray(),
            ValidationFacts = evidence.ValidationFacts.Select(fact => new ValidationFactWire
            {
                Name = fact.Name,
                Outcome = EnumName(fact.Outcome),
                Detail = fact.Detail
            }).ToArray(),
            Effects = new EffectWire
            {
                Categories = evidence.Effects.Categories.Select(EnumName).ToArray(),
                FrameworkState = EnumName(evidence.Effects.FrameworkState),
                ApplicationCallbacks = EnumName(evidence.Effects.ApplicationCallbacks),
                CallbackDetails = evidence.Effects.CallbackDetails.Select(detail => new CallbackWire
                {
                    Name = detail.Name,
                    Count = detail.Count,
                    CountKnown = detail.CountKnown
                }).ToArray(),
                VisibleMutation = EnumName(evidence.Effects.VisibleMutation),
                Operations = evidence.Effects.Operations.ToArray()
            },
            AdapterMetadata = evidence.AdapterMetadata.Select(ToWire).ToArray(),
            Limitations = evidence.Limitations.Select(limitation => new LimitationWire
            {
                Code = limitation.Code,
                Detail = limitation.Detail
            }).ToArray(),
            OperationError = evidence.OperationError is null ? null : ToWire(evidence.OperationError)
        };
    }

    private static TargetWire ToWire(CorrelationTargetRefDto target)
    {
        return new TargetWire
        {
            TargetKind = EnumName(target.TargetKind),
            Managed = target.Managed is null ? null : ToWire(target.Managed),
            Framework = target.Framework is null ? null : ToWire(target.Framework),
            Native = target.Native is null ? null : ToWire(target.Native)
        };
    }

    internal static ManagedObjectWire ToWire(ManagedObjectRefDto managed)
    {
        return new ManagedObjectWire
        {
            Handle = ToWire(managed.Handle),
            TypeIdentity = managed.TypeIdentity is null ? null : ToWire(managed.TypeIdentity),
            BoundaryId = managed.BoundaryId,
            ContextId = managed.ContextId
        };
    }

    private static FrameworkEntityWire ToWire(FrameworkEntityRefDto framework)
    {
        return new FrameworkEntityWire
        {
            AdapterId = framework.AdapterId,
            EntityKind = EnumName(framework.EntityKind),
            LiveHandle = framework.LiveHandle is null ? null : ToWire(framework.LiveHandle),
            Locator = framework.Locator is null ? null : ToWire(framework.Locator),
            GenerationRefs = framework.GenerationRefs.Select(ToWire).ToArray(),
            AdapterMetadata = framework.AdapterMetadata is null ? null : ToWire(framework.AdapterMetadata)
        };
    }

    private static NativeEntityWire ToWire(NativeEntityRefDto native)
    {
        return new NativeEntityWire
        {
            BoundaryKind = EnumName(native.BoundaryKind),
            HwndObservation = native.HwndObservation is null ? null : new HwndWire
            {
                Hwnd = native.HwndObservation.Hwnd,
                HwndGeneration = native.HwndObservation.HwndGeneration
            },
            ProcessId = native.ProcessId,
            ProviderObservationEpoch = native.ProviderObservationEpoch,
            GenerationRefs = native.GenerationRefs.Select(ToWire).ToArray(),
            AdapterMetadata = native.AdapterMetadata is null ? null : ToWire(native.AdapterMetadata)
        };
    }

    internal static HandleWire ToWire(HandleRefDto handle)
    {
        return new HandleWire
        {
            SessionId = handle.SessionId,
            HandleId = handle.HandleId,
            Generation = handle.Generation,
            Kind = EnumName(handle.Kind),
            BoundaryId = handle.BoundaryId
        };
    }

    internal static TypeIdentityWire ToWire(TypeIdentityDto type)
    {
        return new TypeIdentityWire
        {
            TypeId = type.TypeId,
            FullName = type.FullName,
            AssemblySimpleName = type.AssemblySimpleName,
            AssemblyVersion = type.AssemblyVersion,
            AssemblyCulture = type.AssemblyCulture,
            PublicKeyToken = type.PublicKeyToken,
            ModuleVersionId = type.ModuleVersionId,
            BoundaryId = type.BoundaryId,
            DeclaringType = type.DeclaringType is null ? null : ToWire(type.DeclaringType),
            GenericDefinition = type.GenericDefinition is null ? null : ToWire(type.GenericDefinition),
            GenericArguments = type.GenericArguments.Select(ToWire).ToArray(),
            ArrayRank = type.ArrayRank,
            ArrayShape = type.ArrayShape?.ToArray(),
            PointerElementType = type.PointerElementType is null ? null : ToWire(type.PointerElementType),
            ByRefElementType = type.ByRefElementType is null ? null : ToWire(type.ByRefElementType),
            NullableUnderlyingType = type.NullableUnderlyingType is null ? null : ToWire(type.NullableUnderlyingType),
            IsValueType = type.IsValueType,
            BaseType = type.BaseType is null ? null : ToWire(type.BaseType),
            Interfaces = type.Interfaces.Select(ToWire).ToArray(),
            DynamicIdentity = type.DynamicIdentity
        };
    }

    internal static TypeRefWire ToWire(TypeRefDto type)
    {
        return new TypeRefWire { TypeId = type.TypeId, BoundaryId = type.BoundaryId };
    }

    private static GenerationWire ToWire(GenerationRefDto generation)
    {
        return new GenerationWire
        {
            AdapterId = generation.AdapterId,
            Kind = generation.Kind,
            Value = generation.Value,
            ScopeId = generation.ScopeId
        };
    }

    private static MetadataWire ToWire(AdapterMetadataDto metadata)
    {
        return new MetadataWire
        {
            AdapterId = metadata.AdapterId,
            SchemaId = metadata.SchemaId,
            SchemaVersion = metadata.SchemaVersion,
            Payload = ToWire(metadata.Payload)
        };
    }

    private static MetadataValueWire ToWire(DetachedMetadataValueDto value)
    {
        return new MetadataValueWire
        {
            Kind = EnumName(value.Kind),
            BooleanValue = value.BooleanValue,
            IntegerValue = value.IntegerValue,
            DecimalValue = value.DecimalValue,
            FloatingPointValue = value.FloatingPointValue,
            StringValue = value.StringValue,
            ArrayValue = value.ArrayValue?.Select(ToWire).ToArray(),
            ObjectValue = value.ObjectValue?.Select(property => new MetadataPropertyWire
            {
                Name = property.Name,
                Value = ToWire(property.Value)
            }).ToArray()
        };
    }

    private static OperationErrorWire ToWire(OperationErrorDto error)
    {
        return new OperationErrorWire { Code = EnumName(error.Code), Message = error.Message };
    }

    private static FrameworkCorrelationEvidenceDto FromWire(FrameworkEvidenceWire? wire)
    {
        if (wire is null)
        {
            throw new ProtocolJsonException("The framework evidence payload was null.");
        }

        if (wire.EvidenceFacts is null
            || wire.ValidationFacts is null
            || wire.Effects is null
            || wire.AdapterMetadata is null
            || wire.Limitations is null)
        {
            throw new ProtocolJsonException("Framework evidence omitted a required collection or effects payload.");
        }

        return new FrameworkCorrelationEvidenceDto(
            wire.AdapterId,
            wire.ProcessId,
            wire.CandidateTarget is null ? null : FromWire(wire.CandidateTarget),
            ProtocolJsonCollection.MapRequiredElements(
                wire.EvidenceFacts,
                fact => new CorrelationEvidenceFactDto(
                    fact.Name,
                    ParseEnum<ProofOutcome>(fact.Outcome, nameof(fact.Outcome)),
                    ParseEnum<EvidenceKind>(fact.EvidenceKind, nameof(fact.EvidenceKind)),
                    fact.Detail),
                "framework evidence facts"),
            ProtocolJsonCollection.MapRequiredElements(
                wire.ValidationFacts,
                fact => new CorrelationValidationFactDto(
                    fact.Name,
                    ParseEnum<ValidationOutcome>(fact.Outcome, nameof(fact.Outcome)),
                    fact.Detail),
                "framework validation facts"),
            FromWire(wire.Effects),
            ProtocolJsonCollection.MapRequiredElements(
                wire.AdapterMetadata,
                metadata => FromWire(metadata),
                "framework adapter metadata"),
            ProtocolJsonCollection.MapRequiredElements(
                wire.Limitations,
                limitation => new CorrelationLimitationDto(limitation.Code, limitation.Detail),
                "framework limitations"),
            wire.OperationError is null ? null : FromWire(wire.OperationError));
    }

    private static CorrelationTargetRefDto FromWire(TargetWire? wire)
    {
        if (wire is null)
        {
            throw new ProtocolJsonException("The correlation target payload was null.");
        }

        var targetKind = ParseEnum<CorrelationTargetKind>(wire.TargetKind, nameof(wire.TargetKind));
        return targetKind switch
        {
            CorrelationTargetKind.ManagedObject when wire.Managed is not null
                => new CorrelationTargetRefDto(targetKind, managed: FromWire(wire.Managed)),
            CorrelationTargetKind.FrameworkEntity when wire.Framework is not null
                => new CorrelationTargetRefDto(targetKind, framework: FromWire(wire.Framework)),
            CorrelationTargetKind.NativeEntity when wire.Native is not null
                => new CorrelationTargetRefDto(targetKind, native: FromWire(wire.Native)),
            _ => throw new ProtocolJsonException("The target payload does not match its target kind.")
        };
    }

    internal static ManagedObjectRefDto FromWire(ManagedObjectWire? wire)
    {
        if (wire is null || wire.Handle is null)
        {
            throw new ProtocolJsonException("A managed object is missing its handle.");
        }

        return new ManagedObjectRefDto(
            FromWire(wire.Handle),
            wire.TypeIdentity is null ? null : FromWire(wire.TypeIdentity),
            wire.BoundaryId,
            wire.ContextId);
    }

    private static FrameworkEntityRefDto FromWire(FrameworkEntityWire? wire)
    {
        if (wire is null)
        {
            throw new ProtocolJsonException("The framework entity payload was null.");
        }

        return new FrameworkEntityRefDto(
            wire.AdapterId,
            ParseEnum<FrameworkEntityKind>(wire.EntityKind, nameof(wire.EntityKind)),
            wire.LiveHandle is null ? null : FromWire(wire.LiveHandle),
            wire.Locator is null ? null : FromWire(wire.Locator),
            ProtocolJsonCollection.MapRequiredElements(
                wire.GenerationRefs ?? Array.Empty<GenerationWire>(),
                generation => FromWire(generation),
                "framework generation references"),
            wire.AdapterMetadata is null ? null : FromWire(wire.AdapterMetadata));
    }

    private static NativeEntityRefDto FromWire(NativeEntityWire? wire)
    {
        if (wire is null)
        {
            throw new ProtocolJsonException("The native entity payload was null.");
        }

        return new NativeEntityRefDto(
            ParseEnum<NativeBoundaryKind>(wire.BoundaryKind, nameof(wire.BoundaryKind)),
            wire.HwndObservation is null
                ? null
                : new HwndInfoDto(wire.HwndObservation.Hwnd, wire.HwndObservation.HwndGeneration),
            wire.ProcessId,
            wire.ProviderObservationEpoch,
            ProtocolJsonCollection.MapRequiredElements(
                wire.GenerationRefs ?? Array.Empty<GenerationWire>(),
                generation => FromWire(generation),
                "native generation references"),
            wire.AdapterMetadata is null ? null : FromWire(wire.AdapterMetadata));
    }

    internal static HandleRefDto FromWire(HandleWire? wire)
    {
        if (wire is null)
        {
            throw new ProtocolJsonException("The handle payload was null.");
        }

        return new HandleRefDto(
            wire.SessionId,
            wire.HandleId,
            wire.Generation,
            ParseEnum<HandleKind>(wire.Kind, nameof(wire.Kind)),
            wire.BoundaryId);
    }

    internal static TypeIdentityDto FromWire(TypeIdentityWire? wire)
    {
        if (wire is null)
        {
            throw new ProtocolJsonException("The type identity payload was null.");
        }

        if (wire.GenericArguments is null)
        {
            throw new ProtocolJsonException("A type identity is missing genericArguments.");
        }

        if (wire.Interfaces is null)
        {
            throw new ProtocolJsonException("A type identity is missing interfaces.");
        }

        return new TypeIdentityDto(
            wire.TypeId,
            wire.FullName,
            wire.AssemblySimpleName,
            wire.BoundaryId,
            wire.IsValueType
                ?? throw new ProtocolJsonException("A type identity is missing isValueType."),
            ProtocolJsonCollection.MapRequiredElements(
                wire.GenericArguments,
                type => FromWire(type),
                "type generic arguments"),
            ProtocolJsonCollection.MapRequiredElements(
                wire.Interfaces,
                type => FromWire(type),
                "type interfaces"),
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

    internal static TypeRefDto FromWire(TypeRefWire? wire)
    {
        if (wire is null)
        {
            throw new ProtocolJsonException("The type reference payload was null.");
        }

        return new TypeRefDto(wire.TypeId, wire.BoundaryId);
    }

    private static GenerationRefDto FromWire(GenerationWire? wire)
    {
        if (wire is null)
        {
            throw new ProtocolJsonException("The generation reference payload was null.");
        }

        return new GenerationRefDto(wire.AdapterId, wire.Kind, wire.Value, wire.ScopeId);
    }

    private static CorrelationEffectSummaryDto FromWire(EffectWire? wire)
    {
        if (wire is null)
        {
            throw new ProtocolJsonException("The correlation effects payload was null.");
        }

        return new CorrelationEffectSummaryDto(
            (wire.Categories ?? Array.Empty<string>()).Select(value =>
                ParseEnum<EffectCategory>(value, nameof(wire.Categories))),
            ParseEnum<FrameworkStateEffect>(wire.FrameworkState, nameof(wire.FrameworkState)),
            ParseEnum<ApplicationCallbackEffect>(wire.ApplicationCallbacks, nameof(wire.ApplicationCallbacks)),
            ProtocolJsonCollection.MapRequiredElements(
                wire.CallbackDetails ?? Array.Empty<CallbackWire>(),
                detail => new CallbackDetailDto(detail.Name, detail.Count, detail.CountKnown),
                "correlation callback details"),
            ParseEnum<VisibleMutationEffect>(wire.VisibleMutation, nameof(wire.VisibleMutation)),
            wire.Operations ?? Array.Empty<string>());
    }

    private static AdapterMetadataDto FromWire(MetadataWire? wire)
    {
        if (wire is null || wire.Payload is null)
        {
            throw new ProtocolJsonException("The adapter metadata payload is incomplete.");
        }

        return new AdapterMetadataDto(
            wire.AdapterId,
            wire.SchemaId,
            wire.SchemaVersion,
            FromWire(wire.Payload));
    }

    private static DetachedMetadataValueDto FromWire(MetadataValueWire? wire)
    {
        if (wire is null)
        {
            throw new ProtocolJsonException("The metadata value payload was null.");
        }

        var kind = ParseEnum<DetachedMetadataValueKind>(wire.Kind, nameof(wire.Kind));
        return kind switch
        {
            DetachedMetadataValueKind.Null => DetachedMetadataValueDto.Null(),
            DetachedMetadataValueKind.Boolean when wire.BooleanValue.HasValue
                => DetachedMetadataValueDto.Boolean(wire.BooleanValue.Value),
            DetachedMetadataValueKind.Integer when wire.IntegerValue.HasValue
                => DetachedMetadataValueDto.Integer(wire.IntegerValue.Value),
            DetachedMetadataValueKind.Decimal when wire.DecimalValue.HasValue
                => DetachedMetadataValueDto.Decimal(wire.DecimalValue.Value),
            DetachedMetadataValueKind.FloatingPoint when wire.FloatingPointValue.HasValue
                => DetachedMetadataValueDto.FloatingPoint(wire.FloatingPointValue.Value),
            DetachedMetadataValueKind.String when wire.StringValue is not null
                => DetachedMetadataValueDto.String(wire.StringValue),
            DetachedMetadataValueKind.Array when wire.ArrayValue is not null
                => DetachedMetadataValueDto.Array(
                    ProtocolJsonCollection.MapRequiredElements(
                        wire.ArrayValue,
                        value => FromWire(value),
                        "metadata array values")),
            DetachedMetadataValueKind.Object when wire.ObjectValue is not null
                => DetachedMetadataValueDto.Object(
                    ProtocolJsonCollection.MapRequiredElements(
                        wire.ObjectValue,
                        property =>
                        {
                            if (property is null || property.Value is null)
                            {
                                throw new ProtocolJsonException("A metadata object property is incomplete.");
                            }

                            return new DetachedMetadataPropertyDto(property.Name, FromWire(property.Value));
                        },
                        "metadata object properties")),
            _ => throw new ProtocolJsonException("The metadata payload does not match its kind.")
        };
    }

    private static OperationErrorDto FromWire(OperationErrorWire? wire)
    {
        if (wire is null)
        {
            throw new ProtocolJsonException("The operation error payload was null.");
        }

        return new OperationErrorDto(ParseEnum<OperationErrorCode>(wire.Code, nameof(wire.Code)), wire.Message);
    }
}
