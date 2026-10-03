using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using NativeSpy.Agent;
using NativeSpy.Protocol.Common;
using NativeSpy.Protocol.Correlation;
using Xunit;

namespace NativeSpy.Agent.Tests;

public sealed class ClrAgentSessionTests
{
    [Fact]
    public void Registering_the_same_reference_returns_the_same_managed_identity()
    {
        using var session = new ClrAgentSession();
        var target = new EqualValue(7);

        var first = AssertRegistration(session.Register(target));
        var second = AssertRegistration(session.Register(target));

        Assert.Equal(first.Handle.SessionId, second.Handle.SessionId);
        Assert.Equal(first.Handle.HandleId, second.Handle.HandleId);
        Assert.Equal(first.Handle.Generation, second.Handle.Generation);
        Assert.Equal(first.Handle.Kind, second.Handle.Kind);
        Assert.Equal(first.Handle.BoundaryId, second.Handle.BoundaryId);
        Assert.Equal(first.TypeIdentity!.TypeId, second.TypeIdentity!.TypeId);
    }

    [Fact]
    public void Reference_identity_is_used_instead_of_equals()
    {
        using var session = new ClrAgentSession();
        var firstTarget = new EqualValue(7);
        var secondTarget = new EqualValue(7);

        Assert.True(firstTarget.Equals(secondTarget));
        Assert.False(ReferenceEquals(firstTarget, secondTarget));

        var first = AssertRegistration(session.Register(firstTarget));
        var second = AssertRegistration(session.Register(secondTarget));

        Assert.NotEqual(first.Handle.HandleId, second.Handle.HandleId);
        Assert.NotEqual(first.Handle.Generation, second.Handle.Generation);
        Assert.Equal(first.TypeIdentity!.TypeId, second.TypeIdentity!.TypeId);
    }

    [Fact]
    public void Registry_does_not_keep_a_registered_target_alive()
    {
        using var session = new ClrAgentSession();
        var registration = RegisterAndDrop(session);

        Assert.True(WaitForCollection(registration.WeakReference));

        var acquired = session.TryAcquire(registration.Handle);
        Assert.False(acquired.IsSuccess);
        Assert.Equal(OperationErrorCode.ObjectCollected, acquired.Error!.Code);
    }

    [Fact]
    public void Disposing_an_acquisition_releases_operation_local_retention()
    {
        using var session = new ClrAgentSession();
        var operation = AcquireAndDrop(session);

        Assert.True(operation.WeakReference.IsAlive);
        operation.Acquisition.Dispose();
        Assert.True(WaitForCollection(operation.WeakReference));
    }

    [Fact]
    public void Collected_handle_is_never_rebound_to_a_new_object()
    {
        using var session = new ClrAgentSession();
        var original = RegisterAndDrop(session);
        Assert.True(WaitForCollection(original.WeakReference));

        var replacements = Enumerable.Range(0, 12)
            .Select(value => AssertRegistration(session.Register(new EqualValue(value))))
            .ToArray();

        var oldResult = session.TryAcquire(original.Handle);
        Assert.False(oldResult.IsSuccess);
        Assert.Equal(OperationErrorCode.ObjectCollected, oldResult.Error!.Code);
        Assert.DoesNotContain(
            replacements,
            reference => string.Equals(
                reference.Handle.HandleId,
                original.Handle.HandleId,
                StringComparison.Ordinal));
    }

    [Fact]
    public void Sessions_have_independent_identity_namespaces()
    {
        using var firstSession = new ClrAgentSession();
        using var secondSession = new ClrAgentSession();
        var target = new EqualValue(1);

        var first = AssertRegistration(firstSession.Register(target));
        var second = AssertRegistration(secondSession.Register(target));

        Assert.NotEqual(firstSession.SessionId, secondSession.SessionId);
        Assert.NotEqual(first.Handle.SessionId, second.Handle.SessionId);
        Assert.NotEqual(first.Handle.BoundaryId, second.Handle.BoundaryId);

        Assert.Equal(
            OperationErrorCode.InvalidHandle,
            secondSession.TryAcquire(first.Handle).Error!.Code);
        Assert.Equal(
            OperationErrorCode.InvalidHandle,
            firstSession.TryAcquire(second.Handle).Error!.Code);
    }

    [Fact]
    public void Forged_handle_fields_are_rejected_deterministically()
    {
        using var session = new ClrAgentSession();
        var registration = AssertRegistration(session.Register(new EqualValue(1)));
        var handle = registration.Handle;

        var wrongSession = new HandleRefDto(
            "other-session",
            handle.HandleId,
            handle.Generation,
            handle.Kind,
            handle.BoundaryId);
        var unknownId = new HandleRefDto(
            handle.SessionId,
            "clr-object-unknown",
            handle.Generation,
            handle.Kind,
            handle.BoundaryId);
        var wrongKind = new HandleRefDto(
            handle.SessionId,
            handle.HandleId,
            handle.Generation,
            HandleKind.Item,
            handle.BoundaryId);
        var wrongGeneration = new HandleRefDto(
            handle.SessionId,
            handle.HandleId,
            handle.Generation + 1,
            handle.Kind,
            handle.BoundaryId);
        var wrongBoundary = new HandleRefDto(
            handle.SessionId,
            handle.HandleId,
            handle.Generation,
            handle.Kind,
            "other-boundary");

        Assert.Equal(OperationErrorCode.InvalidHandle, session.TryAcquire(wrongSession).Error!.Code);
        Assert.Equal(OperationErrorCode.InvalidHandle, session.TryAcquire(unknownId).Error!.Code);
        Assert.Equal(OperationErrorCode.InvalidHandle, session.TryAcquire(wrongKind).Error!.Code);
        Assert.Equal(OperationErrorCode.StaleHandle, session.TryAcquire(wrongGeneration).Error!.Code);
        Assert.Equal(OperationErrorCode.StaleHandle, session.TryAcquire(wrongBoundary).Error!.Code);
    }

    [Fact]
    public void Closing_a_session_rejects_new_operations_but_not_existing_acquisitions()
    {
        using var session = new ClrAgentSession();
        var registration = AssertRegistration(session.Register(new EqualValue(1)));
        var acquisitionResult = session.TryAcquire(registration.Handle);
        Assert.True(acquisitionResult.IsSuccess);
        using var acquisition = acquisitionResult.Acquisition!;
        var target = acquisition.Target;

        session.Close();
        session.Close();

        Assert.Equal(AgentSessionState.Closed, session.State);
        Assert.Equal(
            OperationErrorCode.SessionClosed,
            session.Register(new EqualValue(2)).Error!.Code);
        Assert.Equal(
            OperationErrorCode.SessionClosed,
            session.TryAcquire(registration.Handle).Error!.Code);
        Assert.Same(target, acquisition.Target);

        acquisition.Dispose();
        acquisition.Dispose();
        Assert.Throws<ObjectDisposedException>(() => _ = acquisition.Target);
        session.Dispose();
    }

    [Fact]
    public void Type_identity_is_session_local_and_boundary_aligned()
    {
        using var session = new ClrAgentSession();
        var first = AssertRegistration(session.Register(new EqualValue(1)));
        var second = AssertRegistration(session.Register(new EqualValue(2)));
        var other = AssertRegistration(session.Register(new DifferentValue(2)));

        Assert.Equal(first.TypeIdentity!.TypeId, second.TypeIdentity!.TypeId);
        Assert.NotEqual(first.TypeIdentity.TypeId, other.TypeIdentity!.TypeId);
        Assert.Equal(first.Handle.BoundaryId, first.TypeIdentity.BoundaryId);
        Assert.Equal(other.Handle.BoundaryId, other.TypeIdentity.BoundaryId);
        Assert.NotEqual(
            typeof(EqualValue).AssemblyQualifiedName,
            first.TypeIdentity.TypeId);
        Assert.Equal(typeof(EqualValue).FullName, first.TypeIdentity.FullName);
    }

    [Fact]
    public void Equivalent_types_in_distinct_load_contexts_have_distinct_boundaries()
    {
        using var session = new ClrAgentSession();
        var assemblyPath = typeof(CrossLoadContextFixture).Assembly.Location;
        using var firstStream = File.OpenRead(assemblyPath);
        using var secondStream = File.OpenRead(assemblyPath);
        var firstLoadContext = new AssemblyLoadContext("agent-test-a", isCollectible: true);
        var secondLoadContext = new AssemblyLoadContext("agent-test-b", isCollectible: true);
        try
        {
            var firstAssembly = firstLoadContext.LoadFromStream(firstStream);
            var secondAssembly = secondLoadContext.LoadFromStream(secondStream);
            var fixtureName = typeof(CrossLoadContextFixture).FullName!;
            var firstType = firstAssembly.GetType(fixtureName, throwOnError: true)!;
            var secondType = secondAssembly.GetType(fixtureName, throwOnError: true)!;
            var firstTarget = Activator.CreateInstance(firstType)!;
            var secondTarget = Activator.CreateInstance(secondType)!;

            var first = AssertRegistration(session.Register(firstTarget));
            var second = AssertRegistration(session.Register(secondTarget));

            Assert.Equal(first.TypeIdentity!.FullName, second.TypeIdentity!.FullName);
            Assert.NotEqual(first.TypeIdentity.TypeId, second.TypeIdentity!.TypeId);
            Assert.NotEqual(first.Handle.BoundaryId, second.Handle.BoundaryId);
        }
        finally
        {
            firstLoadContext.Unload();
            secondLoadContext.Unload();
        }
    }

    [Fact]
    public async Task Concurrent_registration_and_acquisition_are_identity_safe()
    {
        using var session = new ClrAgentSession();
        var sharedTarget = new EqualValue(1);
        var registrations = await Task.WhenAll(
            Enumerable.Range(0, 64)
                .Select(_ => Task.Run(() => session.Register(sharedTarget))));

        var references = registrations.Select(AssertRegistration).ToArray();
        Assert.All(
            references,
            reference => Assert.Equal(references[0].Handle.HandleId, reference.Handle.HandleId));
        Assert.Single(
            references.Select(reference => reference.Handle.HandleId)
                .Distinct(StringComparer.Ordinal));

        var acquisitions = await Task.WhenAll(
            Enumerable.Range(0, 64)
                .Select(_ => Task.Run(() => session.TryAcquire(references[0].Handle))));
        try
        {
            Assert.All(acquisitions, result => Assert.True(result.IsSuccess));
            Assert.All(
                acquisitions,
                result => Assert.Same(sharedTarget, result.Acquisition!.Target));
        }
        finally
        {
            foreach (var result in acquisitions)
            {
                result.Acquisition?.Dispose();
            }
        }
    }

    [Fact]
    public async Task Concurrent_acquisition_and_close_has_only_documented_outcomes()
    {
        using var session = new ClrAgentSession();
        var reference = AssertRegistration(session.Register(new EqualValue(1)));
        var closeTask = Task.Run(session.Close);
        var acquisitionTasks = Enumerable.Range(0, 64)
            .Select(_ => Task.Run(() => session.TryAcquire(reference.Handle)))
            .ToArray();

        await Task.WhenAll(acquisitionTasks.Cast<Task>().Append(closeTask));

        var results = acquisitionTasks.Select(task => task.Result).ToArray();
        Assert.All(
            results,
            result => Assert.True(
                result.IsSuccess
                || result.Error?.Code == OperationErrorCode.SessionClosed));
        foreach (var result in results)
        {
            result.Acquisition?.Dispose();
        }

        Assert.Equal(AgentSessionState.Closed, session.State);
    }

    [Fact]
    public async Task Concurrent_close_has_only_success_or_session_closed_registration_outcomes()
    {
        using var session = new ClrAgentSession();
        var closeTask = Task.Run(session.Close);
        var registrationTasks = Enumerable.Range(0, 64)
            .Select(value => Task.Run(() => session.Register(new EqualValue(value))))
            .ToArray();

        await Task.WhenAll(registrationTasks.Cast<Task>().Append(closeTask));

        var results = registrationTasks.Select(task => task.Result).ToArray();
        Assert.All(
            results,
            result => Assert.True(
                result.IsSuccess
                || result.Error?.Code == OperationErrorCode.SessionClosed));
        var successfulHandles = results
            .Where(result => result.IsSuccess)
            .Select(result => result.Reference!.Handle.HandleId)
            .ToArray();
        Assert.Equal(
            successfulHandles,
            successfulHandles.Distinct(StringComparer.Ordinal).ToArray());
        Assert.Equal(AgentSessionState.Closed, session.State);
    }

    [Fact]
    public void Result_factories_enforce_success_and_error_invariants()
    {
        var handle = new HandleRefDto("session", "handle", 1, HandleKind.ClrObject, "boundary");
        var type = new TypeIdentityDto(
            "type",
            "Example.Type",
            "Example",
            "boundary",
            isValueType: false,
            Array.Empty<TypeRefDto>(),
            Array.Empty<TypeRefDto>());
        var reference = new ManagedObjectRefDto(handle, type, "boundary");

        var registration = ManagedObjectRegistrationResult.Success(reference);
        Assert.True(registration.IsSuccess);
        Assert.Same(reference, registration.Reference);
        Assert.Null(registration.Error);

        var acquisition = ManagedObjectAcquisitionResult.Success(handle, new object());
        Assert.True(acquisition.IsSuccess);
        acquisition.Acquisition!.Dispose();

        Assert.Throws<ArgumentException>(() =>
            ManagedObjectRegistrationResult.Success(new ManagedObjectRefDto(handle)));
        Assert.Throws<ArgumentException>(() =>
            ManagedObjectAcquisitionResult.Success(
                new HandleRefDto("session", "item", 1, HandleKind.Item),
                new object()));
        Assert.Throws<ArgumentNullException>(() =>
            ManagedObjectRegistrationResult.Failure(null!));
        Assert.Throws<ArgumentNullException>(() =>
            ManagedObjectAcquisitionResult.Failure(null!));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (ManagedObjectAcquisition Acquisition, WeakReference WeakReference) AcquireAndDrop(
        ClrAgentSession session)
    {
        var target = new EqualValue(84);
        var weakReference = new WeakReference(target);
        var reference = AssertRegistration(session.Register(target));
        var result = session.TryAcquire(reference.Handle);
        Assert.True(result.IsSuccess);
        return (result.Acquisition!, weakReference);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (HandleRefDto Handle, WeakReference WeakReference) RegisterAndDrop(
        ClrAgentSession session)
    {
        var target = new EqualValue(42);
        var weakReference = new WeakReference(target);
        var reference = AssertRegistration(session.Register(target));
        return (reference.Handle, weakReference);
    }

    private static bool WaitForCollection(WeakReference weakReference)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            if (!weakReference.IsAlive)
            {
                return true;
            }

            Thread.Sleep(10);
        }

        return !weakReference.IsAlive;
    }

    private static ManagedObjectRefDto AssertRegistration(ManagedObjectRegistrationResult result)
    {
        Assert.True(result.IsSuccess, result.Error?.Message);
        return Assert.IsType<ManagedObjectRefDto>(result.Reference);
    }

    private sealed class EqualValue
    {
        public EqualValue(int value)
        {
            Value = value;
        }

        public int Value { get; }

        public override bool Equals(object? obj)
        {
            return obj is EqualValue other && Value == other.Value;
        }

        public override int GetHashCode() => Value;
    }

    private sealed class DifferentValue
    {
        public DifferentValue(int value)
        {
            Value = value;
        }

        public int Value { get; }
    }

    public sealed class CrossLoadContextFixture
    {
        public CrossLoadContextFixture()
        {
        }
    }
}
