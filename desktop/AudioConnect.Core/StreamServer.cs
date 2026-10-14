using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;

namespace AudioConnect.Core;

/// <summary>Single receiver. A slow client never blocks capture or accumulates stale audio.</summary>
public sealed class StreamServer : IAsyncDisposable
{
    private readonly TcpListener listener;
    private readonly byte[] token;
    private readonly CancellationTokenSource lifetime = new();
    private readonly Channel<byte[]> packets = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(3)
    {
        FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true, SingleWriter = true
    });
    private Task? runTask;
    private int connected;
    private int disposed;
    private long sentPackets;
    public bool IsConnected => Volatile.Read(ref connected) != 0;
    public long SentPackets => Interlocked.Read(ref sentPackets);
    public event Action<bool>? ConnectionChanged;
    public event Action<Exception>? Faulted;

    public StreamServer(byte[] pairingToken, int port = AudioProtocol.Port)
    {
        if (pairingToken.Length != AudioProtocol.TokenBytes) throw new ArgumentException("Invalid token.");
        token = (byte[])pairingToken.Clone();
        listener = new TcpListener(IPAddress.Loopback, port);
    }

    public void Start()
    {
        if (runTask is not null) throw new InvalidOperationException("Server has already started.");
        listener.Start(1);
        runTask = RunAsync(lifetime.Token);
    }

    public void Enqueue(byte[] packet)
    {
        if (IsConnected) packets.Writer.TryWrite(packet);
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                using TcpClient client = await listener.AcceptTcpClientAsync(cancellationToken);
                client.NoDelay = true;
                client.SendBufferSize = 8192;
                using NetworkStream stream = client.GetStream();
                try
                {
                    using var handshakeTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    handshakeTimeout.CancelAfter(TimeSpan.FromSeconds(3));
                    var hello = new byte[4 + AudioProtocol.TokenBytes];
                    await stream.ReadExactlyAsync(hello, handshakeTimeout.Token);
                    if (!AudioProtocol.Authenticate(hello, token)) continue;
                    await stream.WriteAsync(AudioProtocol.CreateHeader(), handshakeTimeout.Token);
                    while (packets.Reader.TryRead(out _)) { }
                    Volatile.Write(ref connected, 1);
                    ConnectionChanged?.Invoke(true);
                    while (await packets.Reader.WaitToReadAsync(cancellationToken))
                    {
                        while (packets.Reader.TryRead(out byte[]? packet))
                        {
                            using var writeTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                            writeTimeout.CancelAfter(TimeSpan.FromSeconds(1));
                            await stream.WriteAsync(packet, writeTimeout.Token);
                            Interlocked.Increment(ref sentPackets);
                        }
                    }
                }
                catch (Exception error) when (error is IOException or SocketException or OperationCanceledException) { }
                finally
                {
                    Volatile.Write(ref connected, 0);
                    ConnectionChanged?.Invoke(false);
                }
            }
        }
        catch (Exception error) when (cancellationToken.IsCancellationRequested && error is OperationCanceledException or SocketException) { }
        catch (Exception error) { Faulted?.Invoke(error); }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        await lifetime.CancelAsync();
        listener.Stop();
        packets.Writer.TryComplete();
        if (runTask is not null) await runTask;
        lifetime.Dispose();
        System.Security.Cryptography.CryptographicOperations.ZeroMemory(token);
    }
}
