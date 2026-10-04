using System.Security.Cryptography;
using NativeSpy.Transport.NamedPipes;
using Xunit;

namespace NativeSpy.IntegrationTests;

public sealed class NamedPipeTransportTests
{
    [Fact]
    public async Task Named_pipe_round_trips_a_bounded_frame()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var pipeName = "nativespy-transport-test-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(8));
        await using var server = new NamedPipeServer(new NamedPipeServerOptions(
            pipeName,
            NamedPipeSecurity.GetCurrentUserSid()));
        var acceptTask = server.AcceptAsync();
        await using var client = await NamedPipeClient.ConnectAsync(
            new NamedPipeClientOptions(pipeName),
            TimeSpan.FromSeconds(5));
        await using var serverConnection = await acceptTask;

        var expected = new byte[] { 1, 2, 3, 4, 5 };
        var receiveTask = serverConnection.ReadFrameAsync();
        await client.WriteFrameAsync(expected);
        using var received = await receiveTask;

        Assert.NotNull(received);
        Assert.Equal(expected, received!.Payload.ToArray());
    }
}
