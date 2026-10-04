using System.Security.Cryptography;
using System.Text.Json;
using NativeSpy.Agent;
using NativeSpy.Agent.Host;
using NativeSpy.Client.NamedPipes;
using NativeSpy.Protocol.Common;
using NativeSpy.Protocol.Json;
using NativeSpy.Transport.NamedPipes;
using Xunit;

namespace NativeSpy.IntegrationTests;

public sealed class AgentHostCompositionValidationTests
{
    [Theory]
    [InlineData("echo")]
    [InlineData("response")]
    [InlineData("session.close")]
    public async Task Invalid_operation_declarations_fail_before_listener_startup(string operationName)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var options = CreateOptions();
        await using var host = new AgentHost(
            options,
            new TestCompositionFactory(
                new[] { operationName },
                new TestHandler(operationName)));

        Assert.Throws<InvalidOperationException>(() => host.StartAsync().GetAwaiter().GetResult());
        Assert.Equal(AgentHostState.Created, host.State);
    }

    [Fact]
    public async Task Duplicate_operation_declarations_fail_before_listener_startup()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var options = CreateOptions();
        await using var host = new AgentHost(
            options,
            new TestCompositionFactory(
                new[] { "test.echo", "test.echo" },
                new TestHandler("test.echo")));

        Assert.Throws<InvalidOperationException>(() => host.StartAsync().GetAwaiter().GetResult());
        Assert.Equal(AgentHostState.Created, host.State);
    }

    [Fact]
    public async Task Duplicate_actual_handlers_are_rejected_during_activation()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var options = CreateOptions();
        await using var host = new AgentHost(
            options,
            new TestCompositionFactory(
                new[] { "test.echo" },
                new TestHandler("test.echo"),
                new TestHandler("test.echo")));
        await host.StartAsync();

        var identity = options.TargetProcessIdentity;
        var exception = await Assert.ThrowsAsync<NamedPipeProtocolException>(() =>
            NamedPipeClientSession.ConnectAsync(
                host.BootstrapDescriptor,
                identity,
                cancellationToken: CancellationToken.None));

        Assert.Equal(ProtocolErrorCode.InternalFailure, exception.Error.Code);
        Assert.Equal(AgentHostState.Closed, host.State);
    }

    [Fact]
    public async Task Actual_handlers_must_match_fixed_declarations()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var options = CreateOptions();
        await using var host = new AgentHost(
            options,
            new TestCompositionFactory(
                new[] { "test.echo" },
                new TestHandler("test.other")));
        await host.StartAsync();

        var exception = await Assert.ThrowsAsync<NamedPipeProtocolException>(() =>
            NamedPipeClientSession.ConnectAsync(
                host.BootstrapDescriptor,
                options.TargetProcessIdentity,
                cancellationToken: CancellationToken.None));

        Assert.Equal(ProtocolErrorCode.InternalFailure, exception.Error.Code);
        Assert.Equal(AgentHostState.Closed, host.State);
    }

    [Fact]
    public async Task Listener_creation_failure_fails_startup_before_listening()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var options = CreateOptions();
        await using var host = new AgentHost(
            options,
            new TestCompositionFactory(
                new[] { "test.echo" },
                new TestHandler("test.echo")),
            _ => throw new InvalidOperationException("ACL creation failed."));

        Assert.Throws<InvalidOperationException>(() => host.StartAsync().GetAwaiter().GetResult());
        Assert.Equal(AgentHostState.Created, host.State);
    }

    [Fact]
    public async Task Client_rejects_a_bootstrap_descriptor_for_a_different_process_instance()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var bootstrap = new BootstrapDescriptorWire
        {
            PipeName = "nativespy-never-connected",
            BootstrapNonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_'),
            TargetProcessIdentity = new ProcessIdentityWire
            {
                ProcessId = Environment.ProcessId,
                ProcessStartIdentity = "different-process-instance"
            },
            MinSupportedVersion = 1,
            MaxSupportedVersion = 1
        };
        var expected = ProcessIdentityReader.ReadCurrent();

        var exception = await Assert.ThrowsAsync<NamedPipeSessionException>(() =>
            NamedPipeClientSession.ConnectAsync(
                bootstrap,
                expected,
                cancellationToken: CancellationToken.None));

        Assert.Equal(OperationErrorCode.TargetExited, exception.Code);
    }

    private static AgentHostOptions CreateOptions()
    {
        return new AgentHostOptions(
            "nativespy-validation-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(8)),
            RandomNumberGenerator.GetBytes(32),
            ProcessIdentityReader.ReadCurrent(),
            NamedPipeSecurity.GetCurrentUserSid(),
            bootstrapLifetime: TimeSpan.FromSeconds(30));
    }

    private sealed class TestCompositionFactory : IAgentHostCompositionFactory
    {
        private readonly IReadOnlyList<IAgentOperationHandler> _handlers;

        public TestCompositionFactory(
            IReadOnlyList<string> declaredOperationNames,
            params IAgentOperationHandler[] handlers)
        {
            DeclaredOperationNames = declaredOperationNames;
            _handlers = handlers;
        }

        public IReadOnlyList<string> DeclaredOperationNames { get; }

        public IAgentHostComposition Create(
            IManagedObjectReferenceService identityService,
            HostSessionContext context)
        {
            return new TestComposition(_handlers);
        }
    }

    private sealed class TestComposition : IAgentHostComposition
    {
        public TestComposition(IReadOnlyList<IAgentOperationHandler> handlers)
        {
            Handlers = handlers;
        }

        public IReadOnlyList<IAgentOperationHandler> Handlers { get; }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class TestHandler : IAgentOperationHandler
    {
        public TestHandler(string operationName)
        {
            OperationName = operationName;
        }

        public string OperationName { get; }

        public Task<AgentHandlerResult> HandleAsync(
            AgentRequestContext context,
            JsonElement payload,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(AgentHandlerResult.Success(payload));
        }
    }
}
