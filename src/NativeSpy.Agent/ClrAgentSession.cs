using System.Runtime.Loader;
using NativeSpy.Agent.Internal;
using NativeSpy.Protocol.Common;
using NativeSpy.Protocol.Correlation;

namespace NativeSpy.Agent;

public sealed class ClrAgentSession : IManagedObjectReferenceService, IDisposable
{
    private readonly object _gate = new();
    private readonly string _sessionId = $"session-{Guid.NewGuid():N}";
    private readonly ObjectIdentityRegistry _objectRegistry = new();
    private readonly RuntimeBoundaryRegistry _boundaryRegistry = new();
    private readonly TypeIdentityRegistry _typeRegistry = new();
    private AgentSessionState _state = AgentSessionState.Active;
    private long _nextHandleId;
    private long _nextGeneration;
    private long _nextTypeId;

    public string SessionId => _sessionId;

    public AgentSessionState State
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    public ManagedObjectRegistrationResult Register(object target)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (!IsActive())
        {
            return SessionClosedRegistration();
        }

        var observation = ObserveType(target.GetType());
        if (observation is null)
        {
            lock (_gate)
            {
                return _state == AgentSessionState.Active
                    ? ManagedObjectRegistrationResult.Failure(
                        new OperationErrorDto(
                            OperationErrorCode.RuntimeUnavailable,
                            "The target type has no available AssemblyLoadContext."))
                    : SessionClosedRegistration();
            }
        }

        lock (_gate)
        {
            if (_state != AgentSessionState.Active)
            {
                return SessionClosedRegistration();
            }

            if (_objectRegistry.TryGet(target, out var existing))
            {
                return ManagedObjectRegistrationResult.Success(existing!.Reference);
            }

            var boundary = _boundaryRegistry.GetOrCreate(
                observation.LoadContext,
                () => $"boundary-{Guid.NewGuid():N}",
                DescribeBoundary(observation.LoadContext));
            var typeIdentity = GetOrCreateTypeIdentity(observation, boundary.BoundaryId);
            var handle = new HandleRefDto(
                _sessionId,
                $"clr-object-{NextPositive(ref _nextHandleId, "handle ID")}",
                NextPositive(ref _nextGeneration, "handle generation"),
                HandleKind.ClrObject,
                boundary.BoundaryId);
            var reference = new ManagedObjectRefDto(
                handle,
                typeIdentity,
                boundary.BoundaryId);

            _objectRegistry.Add(target, reference);
            return ManagedObjectRegistrationResult.Success(reference);
        }
    }

    public ManagedObjectAcquisitionResult TryAcquire(HandleRefDto handle)
    {
        ArgumentNullException.ThrowIfNull(handle);

        lock (_gate)
        {
            if (_state != AgentSessionState.Active)
            {
                return Failure(OperationErrorCode.SessionClosed, "The Agent session is closed.");
            }

            if (!string.Equals(handle.SessionId, _sessionId, StringComparison.Ordinal))
            {
                return Failure(OperationErrorCode.InvalidHandle, "The handle belongs to a different session.");
            }

            if (!_objectRegistry.TryGetRecord(handle.HandleId, out var record))
            {
                return Failure(OperationErrorCode.InvalidHandle, "The handle ID is not known to this session.");
            }

            if (handle.Kind != HandleKind.ClrObject)
            {
                return Failure(OperationErrorCode.InvalidHandle, "The handle kind is not a CLR object handle.");
            }

            if (handle.Generation != record!.Handle.Generation)
            {
                return Failure(OperationErrorCode.StaleHandle, "The handle generation is stale.");
            }

            if (!string.Equals(handle.BoundaryId, record.Handle.BoundaryId, StringComparison.Ordinal))
            {
                return Failure(OperationErrorCode.StaleHandle, "The handle runtime boundary is stale.");
            }

            if (record.State == HandleRecordState.Collected)
            {
                return Failure(OperationErrorCode.ObjectCollected, "The referenced CLR object has been collected.");
            }

            if (!record.Target.TryGetTarget(out var target))
            {
                record.MarkCollected();
                return Failure(OperationErrorCode.ObjectCollected, "The referenced CLR object has been collected.");
            }

            return ManagedObjectAcquisitionResult.Success(record.Handle, target);
        }
    }

    public void Close()
    {
        lock (_gate)
        {
            if (_state == AgentSessionState.Closed)
            {
                return;
            }

            _state = AgentSessionState.Closing;
            _objectRegistry.Clear();
            _typeRegistry.Clear();
            _boundaryRegistry.Clear();
            _state = AgentSessionState.Closed;
        }
    }

    public void Dispose()
    {
        Close();
    }

    private TypeIdentityDto GetOrCreateTypeIdentity(
        TypeObservation observation,
        string boundaryId)
    {
        if (_typeRegistry.TryGet(observation.Type, out var existing))
        {
            if (!string.Equals(
                    existing!.Identity.BoundaryId,
                    boundaryId,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "A CLR Type was associated with more than one runtime boundary.");
            }

            return existing.Identity;
        }

        var typeIdentity = new TypeIdentityDto(
            $"clr-type-{NextPositive(ref _nextTypeId, "type ID")}",
            observation.FullName,
            observation.AssemblySimpleName,
            boundaryId,
            observation.Type.IsValueType,
            Array.Empty<TypeRefDto>(),
            Array.Empty<TypeRefDto>(),
            assemblyVersion: observation.AssemblyVersion,
            moduleVersionId: observation.ModuleVersionId);
        _typeRegistry.Add(observation.Type, typeIdentity);
        return typeIdentity;
    }

    private bool IsActive()
    {
        lock (_gate)
        {
            return _state == AgentSessionState.Active;
        }
    }

    private static TypeObservation? ObserveType(Type type)
    {
        var assembly = type.Assembly;
        var loadContext = AssemblyLoadContext.GetLoadContext(assembly);
        if (loadContext is null)
        {
            return null;
        }

        var assemblyName = assembly.GetName();
        var fullName = type.FullName ?? type.Name;
        var assemblySimpleName = assemblyName.Name
            ?? assembly.FullName
            ?? type.Name;
        var assemblyVersion = assemblyName.Version?.ToString();
        var moduleVersionId = TryGetModuleVersionId(type);
        return new TypeObservation(
            type,
            loadContext,
            fullName,
            assemblySimpleName,
            assemblyVersion,
            moduleVersionId);
    }

    private static string? TryGetModuleVersionId(Type type)
    {
        try
        {
            return type.Module.ModuleVersionId.ToString("D");
        }
        catch (Exception exception) when (IsOptionalMetadataFailure(exception))
        {
            return null;
        }
    }

    private static bool IsOptionalMetadataFailure(Exception exception)
    {
        return exception is InvalidOperationException
            or NotImplementedException
            or NotSupportedException
            or FileNotFoundException;
    }

    private static string? DescribeBoundary(AssemblyLoadContext loadContext)
    {
        if (ReferenceEquals(loadContext, AssemblyLoadContext.Default))
        {
            return "AssemblyLoadContext.Default";
        }

        return string.IsNullOrWhiteSpace(loadContext.Name)
            ? "AssemblyLoadContext"
            : loadContext.Name;
    }

    private static long NextPositive(ref long counter, string name)
    {
        if (counter == long.MaxValue)
        {
            throw new InvalidOperationException($"The session exhausted its {name} allocation space.");
        }

        return ++counter;
    }

    private static ManagedObjectRegistrationResult SessionClosedRegistration()
    {
        return ManagedObjectRegistrationResult.Failure(
            new OperationErrorDto(OperationErrorCode.SessionClosed, "The Agent session is closed."));
    }

    private static ManagedObjectAcquisitionResult Failure(
        OperationErrorCode code,
        string message)
    {
        return ManagedObjectAcquisitionResult.Failure(new OperationErrorDto(code, message));
    }
}
