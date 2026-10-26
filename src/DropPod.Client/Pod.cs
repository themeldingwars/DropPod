using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Aero.Protocol;
using DropPod.Client.Net;
using DropPod.Client.Web;

namespace DropPod.Client;

/// <summary>
/// One headless client. <see cref="DropAsync"/> takes it from the oracle ticket to the Login on the game server,
/// then it acks and counts what the server sends.
/// </summary>
/// <remarks>It doesn't answer EnterZone yet, so it never gets into the world.</remarks>
public sealed class Pod : IAsyncDisposable
{
    /// <summary>Not checked against the client's rate yet.</summary>
    private static readonly TimeSpan TimeSyncInterval = TimeSpan.FromSeconds(5);

    private readonly ClientProtocol _protocol;
    private readonly Stopwatch _uptime = Stopwatch.StartNew();
    private readonly ConcurrentDictionary<string, int> _received = new();
    private readonly CancellationTokenSource _stop = new();
    private UdpClient? _socket;
    private MatrixSession? _session;
    private Task? _receiveLoop;
    private Task? _timeSyncLoop;
    private ushort _matrixSequence;

    public Pod(string name, Red5Credentials account, ulong characterGuid, ClientProtocol protocol)
    {
        Name = name;
        Account = account;
        CharacterGuid = characterGuid;
        _protocol = protocol;
    }

    public string Name { get; }

    public Red5Credentials Account { get; }

    public ulong CharacterGuid { get; }

    public uint SocketId => _session?.SocketId ?? 0;

    /// <summary>Message names (Matrix) and frame counts (GSS), for the run summary.</summary>
    public IReadOnlyDictionary<string, int> Received => _received;

    /// <summary>Set when the server sent CloseConnection.</summary>
    public uint? ServerShutdownCode { get; private set; }

    public event Action<Pod, string>? Log;

    public async Task DropAsync(RinClient rin, CancellationToken ct)
    {
        var ticket = await rin.GetOracleTicketAsync(Account, CharacterGuid, ct);
        var addresses = await Dns.GetHostAddressesAsync(ticket.MatrixHost, ct);
        var matrix = new IPEndPoint(addresses.First(a => a.AddressFamily == AddressFamily.InterNetwork), ticket.MatrixPort);

        _socket = new UdpClient(0, AddressFamily.InterNetwork);
        _session = await MatrixHandshake.RunAsync(_socket, matrix, _protocol, ct);
        _matrixSequence = _session.MatrixSequenceStart;
        Log?.Invoke(this, $"socket {_session.SocketId:x8}, game server {_session.GameServer}");

        _receiveLoop = ReceiveLoopAsync(_stop.Token);
        _timeSyncLoop = TimeSyncLoopAsync(_stop.Token);

        await SendControlAsync(ControlMessages.TimeSync(Microseconds()), ct);
        await SendMatrixAsync(MatrixMessages.Login(CharacterGuid, ticket.Ticket), MatrixMessage.Login, ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (_socket != null && _session != null && ServerShutdownCode == null)
        {
            try
            {
                await SendControlAsync(ControlMessages.Close(), CancellationToken.None);
            }
            catch (SocketException)
            {
            }
        }

        await _stop.CancelAsync();
        foreach (var loop in new[] { _receiveLoop, _timeSyncLoop })
        {
            if (loop != null)
            {
                try
                {
                    await loop;
                }
                catch (OperationCanceledException)
                {
                }
            }
        }

        _socket?.Dispose();
        _stop.Dispose();
    }

    private async Task SendMatrixAsync(Aero.Gen.IAero message, MatrixMessage kind, CancellationToken ct)
    {
        var payload = MatrixMessages.Encode(message, kind, _protocol.Matrix);
        await SendFrameAsync(Channel.Matrix, _matrixSequence++, payload, ct);
    }

    private Task SendControlAsync(byte[] payload, CancellationToken ct) => SendFrameAsync(Channel.Control, 0, payload, ct);

    private async Task SendFrameAsync(Channel channel, ushort sequence, byte[] payload, CancellationToken ct)
    {
        var datagram = Framing.Datagram(_session!.SocketId, channel, sequence, payload);
        await _socket!.SendAsync(datagram, _session.GameServer, ct);
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && ServerShutdownCode == null)
        {
            var result = await _socket!.ReceiveAsync(ct);
            foreach (var frame in Framing.Parse(result.Buffer, out var socketId))
            {
                if (socketId != _session!.SocketId)
                {
                    continue;
                }

                await HandleFrameAsync(frame, ct);
            }
        }
    }

    private async Task HandleFrameAsync(Frame frame, CancellationToken ct)
    {
        switch (frame.Channel)
        {
            case Channel.Control:
                if (ControlMessages.TryReadClose(frame.Payload.Span, out var code))
                {
                    ServerShutdownCode = code;
                    Log?.Invoke(this, $"server closed the connection, code {code}");
                }

                break;
            case Channel.Matrix:
                await SendControlAsync(ControlMessages.Ack(Channel.Matrix, frame.Sequence!.Value), ct);
                var name = frame.IsSplit ? "Matrix (split)"
                         : MatrixMessages.TryDecode(frame.Payload, _protocol.Matrix, out var kind, out _) ? kind.ToString()
                         : $"Matrix 0x{frame.Payload.Span[0]:x2}";
                Count(name);
                break;
            case Channel.ReliableGss:
                await SendControlAsync(ControlMessages.Ack(Channel.ReliableGss, frame.Sequence!.Value), ct);
                Count("ReliableGss");
                break;
            case Channel.UnreliableGss:
                Count("UnreliableGss");
                break;
        }
    }

    private void Count(string name)
    {
        if (_received.AddOrUpdate(name, 1, (_, n) => n + 1) == 1)
        {
            Log?.Invoke(this, $"first {name}");
        }
    }

    private async Task TimeSyncLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSyncInterval);
        while (await timer.WaitForNextTickAsync(ct) && ServerShutdownCode == null)
        {
            await SendControlAsync(ControlMessages.TimeSync(Microseconds()), ct);
        }
    }

    private ulong Microseconds() => (ulong)(_uptime.Elapsed.Ticks / (TimeSpan.TicksPerMillisecond / 1000));
}
