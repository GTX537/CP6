using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace CP6.Tests.Erp;

// A loopback-only PostgreSQL fixture. Drop exactly one acknowledged COMMIT from the selected LOGIN.
internal sealed class StockPostgresCommitRelay : IAsyncDisposable
{
    private const int DropNextCommit = 1;
    private const int HoldNextCommit = 2;
    private readonly TcpListener listener;
    private readonly CancellationTokenSource stopping = new();
    private readonly TaskCompletionSource dropped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource commitHeld = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource releaseCommit = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<Task> connections = [];
    private readonly Task accepting;
    private int armed;

    internal StockPostgresCommitRelay(int postgresPort, string login, int listenPort = 0)
    {
        listener = new TcpListener(IPAddress.Loopback, listenPort);
        listener.Start();
        accepting = AcceptAsync(postgresPort, login);
    }

    internal int Port => ((IPEndPoint)listener.LocalEndpoint).Port;
    internal Task Dropped => dropped.Task;
    internal Task CommitHeld => commitHeld.Task;
    internal void Arm() => Interlocked.Exchange(ref armed, DropNextCommit);
    internal void ArmHold() => Interlocked.Exchange(ref armed, HoldNextCommit);
    internal void ReleaseCommit() => releaseCommit.TrySetResult();

    private async Task AcceptAsync(int postgresPort, string login)
    {
        try
        {
            while (true)
            {
                var client = await listener.AcceptTcpClientAsync(stopping.Token);
                connections.Add(ForwardAsync(client, postgresPort, login));
            }
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
    }

    private async Task ForwardAsync(TcpClient client, int postgresPort, string login)
    {
        using (client)
        using (var postgres = new TcpClient())
        using (var lifetime = CancellationTokenSource.CreateLinkedTokenSource(stopping.Token))
        {
            var ct = lifetime.Token;
            try
            {
                await postgres.ConnectAsync(IPAddress.Loopback, postgresPort, ct);
                var front = client.GetStream();
                var back = postgres.GetStream();
                var header = new byte[4];
                if (!await ReadHeaderAsync(front, header, ct)) return;
                var startup = new byte[BodyLength(header)];
                await front.ReadExactlyAsync(startup, ct);
                await back.WriteAsync(header, ct);
                await back.WriteAsync(startup, ct);

                // The fixture disables TLS. Startup fields contain the LOGIN but no authentication secret.
                var fields = Encoding.UTF8.GetString(startup.AsSpan(4)).Split('\0');
                var selected = false;
                for (var i = 0; i + 1 < fields.Length; i += 2)
                    if (fields[i] == "user") selected = fields[i + 1] == login;
                var requests = front.CopyToAsync(back, ct);
                var responses = selected ? ForwardWriterResponsesAsync(back, front, ct) : back.CopyToAsync(front, ct);
                await Task.WhenAny(requests, responses);
                await lifetime.CancelAsync();
                await Task.WhenAll(requests, responses);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (IOException error) when (error.InnerException is SocketException
                { SocketErrorCode: SocketError.ConnectionReset or SocketError.ConnectionAborted }) { }
        }
    }

    private async Task ForwardWriterResponsesAsync(Stream postgres, Stream client, CancellationToken ct)
    {
        var header = new byte[5];
        while (await ReadHeaderAsync(postgres, header, ct))
        {
            var body = new byte[BodyLength(header.AsSpan(1))];
            await postgres.ReadExactlyAsync(body, ct);
            if (header[0] == 'C' && body.AsSpan().SequenceEqual("COMMIT\0"u8))
            {
                switch (Interlocked.Exchange(ref armed, 0))
                {
                    case DropNextCommit:
                        dropped.TrySetResult();
                        return;
                    case HoldNextCommit:
                        // PostgreSQL has finished committing. The client is still waiting for this reply.
                        commitHeld.TrySetResult();
                        await releaseCommit.Task.WaitAsync(ct);
                        break;
                }
            }
            await client.WriteAsync(header, ct);
            await client.WriteAsync(body, ct);
        }
    }

    private static async Task<bool> ReadHeaderAsync(Stream stream, byte[] header, CancellationToken ct)
    {
        if (await stream.ReadAsync(header.AsMemory(0, 1), ct) == 0) return false;
        await stream.ReadExactlyAsync(header.AsMemory(1), ct);
        return true;
    }

    private static int BodyLength(ReadOnlySpan<byte> header)
    {
        var length = BinaryPrimitives.ReadInt32BigEndian(header);
        return length is >= 4 and <= 8 * 1024 * 1024 ? length - 4 : throw new IOException("Invalid owned PostgreSQL frame.");
    }

    public async ValueTask DisposeAsync()
    {
        await stopping.CancelAsync();
        try
        {
            await accepting;
            await Task.WhenAll(connections);
        }
        finally
        {
            listener.Dispose();
            stopping.Dispose();
        }
    }
}
