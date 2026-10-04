using System.Buffers.Binary;
using System.IO.Pipes;
using System.Security.Cryptography;
using NativeSpy.Transport.NamedPipes;
using Xunit;

namespace NativeSpy.IntegrationTests;

public sealed class NamedPipeTransportTests
{
    [Fact]
    public async Task Named_pipe_round_trips_multiple_bounded_frames()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        await using var server = CreateServer();
        server.Bind();
        var acceptTask = server.AcceptAsync();
        await using var client = await NamedPipeClient.ConnectAsync(
            new NamedPipeClientOptions(server.PipeName),
            TimeSpan.FromSeconds(5));
        await using var serverConnection = await acceptTask;

        for (var index = 0; index < 3; index++)
        {
            var expected = new byte[] { (byte)index, 1, 2, 3 };
            var receiveTask = serverConnection.ReadFrameAsync();
            await client.WriteFrameAsync(expected);
            using var received = await receiveTask;
            Assert.Equal(expected, received!.Payload.ToArray());
        }
    }

    [Fact]
    public async Task Concurrent_writes_are_received_as_complete_serialized_frames()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        await using var server = CreateServer();
        server.Bind();
        var acceptTask = server.AcceptAsync();
        await using var client = await NamedPipeClient.ConnectAsync(
            new NamedPipeClientOptions(server.PipeName),
            TimeSpan.FromSeconds(5));
        await using var serverConnection = await acceptTask;

        var payloads = Enumerable.Range(0, 16)
            .Select(index => new byte[] { (byte)index, 10, 20, 30 })
            .ToArray();
        var receiveTask = Task.Run(async () =>
        {
            var frames = new List<byte[]>();
            for (var index = 0; index < payloads.Length; index++)
            {
                using var frame = await serverConnection.ReadFrameAsync();
                frames.Add(frame!.Payload.ToArray());
            }

            return frames;
        });
        var writes = payloads.Select(payload => client.WriteFrameAsync(payload));
        await Task.WhenAll(writes);
        var received = await receiveTask;

        Assert.Equal(
            payloads.OrderBy(payload => payload[0]).Select(payload => payload.ToArray()),
            received.OrderBy(payload => payload[0]));
    }

    [Fact]
    public async Task Zero_and_oversized_prefixes_are_rejected_before_payload_read()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        await AssertPrefixRejectedAsync(0, maximumFrameBytes: 32);
        await AssertPrefixRejectedAsync(33, maximumFrameBytes: 32);
    }

    [Fact]
    public async Task Truncated_prefix_and_payload_are_terminal_frame_errors()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        await AssertTruncatedAsync(new byte[] { 0, 0 });
        await AssertTruncatedAsync(new byte[] { 0, 0, 0, 5, 1, 2 });
    }

    [Fact]
    public async Task Disconnect_during_read_or_write_is_terminal()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        await using var server = CreateServer();
        server.Bind();
        var acceptTask = server.AcceptAsync();
        await using var client = await NamedPipeClient.ConnectAsync(
            new NamedPipeClientOptions(server.PipeName),
            TimeSpan.FromSeconds(5));
        await using var serverConnection = await acceptTask;

        var readTask = serverConnection.ReadFrameAsync();
        client.Abort();
        Assert.Null(await readTask);

        await Assert.ThrowsAnyAsync<Exception>(() =>
            serverConnection.WriteFrameAsync(new byte[] { 1, 2, 3 }));
    }

    private static NamedPipeServer CreateServer(int maximumFrameBytes = 1024 * 1024)
    {
        return new NamedPipeServer(new NamedPipeServerOptions(
            "nativespy-frame-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(8)),
            NamedPipeSecurity.GetCurrentUserSid(),
            maximumFrameBytes));
    }

    private static async Task AssertPrefixRejectedAsync(uint length, int maximumFrameBytes)
    {
        await using var server = CreateServer(maximumFrameBytes);
        server.Bind();
        var acceptTask = server.AcceptAsync();
        await using var client = await ConnectRawAsync(server.PipeName);
        await using var serverConnection = await acceptTask;

        var prefix = new byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32BigEndian(prefix, length);
        var readTask = serverConnection.ReadFrameAsync();
        await client.WriteAsync(prefix);

        await Assert.ThrowsAsync<NamedPipeFrameException>(() => readTask);
    }

    private static async Task AssertTruncatedAsync(byte[] bytes)
    {
        await using var server = CreateServer();
        server.Bind();
        var acceptTask = server.AcceptAsync();
        await using var client = await ConnectRawAsync(server.PipeName);
        await using var serverConnection = await acceptTask;

        var readTask = serverConnection.ReadFrameAsync();
        await client.WriteAsync(bytes);
        await client.FlushAsync();
        client.Dispose();

        await Assert.ThrowsAsync<NamedPipeFrameException>(() => readTask);
    }

    private static async Task<NamedPipeClientStream> ConnectRawAsync(string pipeName)
    {
        var client = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        try
        {
            await client.ConnectAsync(5_000);
            return client;
        }
        catch
        {
            await client.DisposeAsync();
            throw;
        }
    }
}
