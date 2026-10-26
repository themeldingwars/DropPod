using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using Aero.Gen;
using Aero.Protocol;
using AeroMessages.GSS.Character;
using AeroMessages.GSS.Character.Command;
using AeroMessages.GSS.Character.Event;
using AeroMessages.GSS.Generic;
using AeroMessages.Matrix;
using DropPod.Client.Net;
using DropPod.Client.Recording;
using DropPod.Client.Web;
using DropPod.Client.World;

namespace DropPod.Client;

public enum PodState
{
    Created,
    LoggingIn,
    LoadingZone,
    EnteringWorld,
    InWorld,
    Closed,
}

/// <summary>
/// One headless client. <see cref="DropAsync"/> takes it from the oracle ticket to the Login, then it does what the
/// 1962 client does standing still: load the zone, ack it, report ready once its character arrived and send its pose
/// and update requests. Everything runs on one loop, so nothing in here needs a lock.
/// </summary>
public sealed class Pod : IAsyncDisposable
{
    private const short StandingMovementState = 4096; // what the client sent while standing
    private const ushort FullJetpack = 25500;
    private const byte InterpolationDelay = 40; // ceil(network.interpolationDelay / 5), the client sent 37 to 44

    private readonly ClientProtocol _protocol;
    private readonly PodTimings _timings;
    private readonly Stopwatch _uptime = Stopwatch.StartNew();
    private readonly CancellationTokenSource _stop = new();
    private readonly Outbox _outbox = new();
    private readonly EntityTable _world = new();
    private readonly Dictionary<Channel, InboundChannel> _inbound = [];
    private readonly Dictionary<Channel, OutboundChannel> _outbound = [];
    private readonly byte _baseControllerTypecode;
    private readonly byte _forcedMovementTypecode;
    private readonly byte _forcedMovementId;

    private UdpClient? _socket;
    private IPEndPoint? _local;
    private MatrixSession? _session;
    private Task? _loop;
    private GameClock? _clock;
    private ulong _instanceId;
    private DateTime _droppedAt;
    private DateTime _enterZoneAt;
    private DateTime _enterZoneAckAt;
    private DateTime _readyAt;
    private bool _streamingSent;
    private TimeSpan _lastTimeSyncSent = TimeSpan.FromDays(-1);
    private TimeSpan _lastTimeSyncReceived = TimeSpan.FromDays(-1);
    private DateTime _lastClientStatus;
    private DateTime _nextMovement;
    private DateTime _lastKeyframeRequest;
    private long _bytesDown;
    private long _bytesUp;

    public Pod(string name, Red5Credentials account, ulong characterGuid, ClientProtocol protocol, PodTimings? timings = null)
    {
        Name = name;
        Account = account;
        CharacterGuid = characterGuid;
        _protocol = protocol;
        _timings = timings ?? new PodTimings();

        GssTables.TryGetWireIds(protocol.Gss, GssCharacterView.BaseController, GssCharacterCommand.PlayerReady, out _baseControllerTypecode, out _);
        GssTables.TryGetWireIds(protocol.Gss, GssCharacterView.CombatController, GssCharacterMessage.ForcedMovement, out _forcedMovementTypecode, out _forcedMovementId);
    }

    public string Name { get; }

    public Red5Credentials Account { get; }

    public ulong CharacterGuid { get; }

    private ulong CharacterEntity => CharacterGuid & 0xffffffffffffff00;

    public PodState State { get; private set; }

    public uint SocketId => _session?.SocketId ?? 0;

    public uint ZoneId { get; private set; }

    public Vector3? Position { get; private set; }

    public long DatagramsSent { get; private set; }

    public long DatagramsReceived { get; private set; }

    /// <summary>Set before the drop, the handshake gets recorded too.</summary>
    public PcapngWriter? Recorder { get; init; }

    /// <summary>Drop to EnterZone, drop to the first PlayerReady.</summary>
    public TimeSpan? TimeToZone { get; private set; }

    public TimeSpan? TimeToWorld { get; private set; }

    /// <summary>Round trip of the last time sync.</summary>
    public TimeSpan? RoundTrip { get; private set; }

    public uint? ServerShutdownCode { get; private set; }

    public string? Failure { get; private set; }

    public EntityTable World => _world;

    public IReadOnlyDictionary<Channel, InboundChannel> Inbound => _inbound;

    public IReadOnlyDictionary<Channel, OutboundChannel> Outbound => _outbound;

    public event Action<Pod, string>? Log;

    public async Task DropAsync(RinClient rin, CancellationToken ct)
    {
        State = PodState.LoggingIn;
        _droppedAt = DateTime.UtcNow;

        var ticket = await rin.GetOracleTicketAsync(Account, CharacterGuid, ct);
        var addresses = await Dns.GetHostAddressesAsync(ticket.MatrixHost, ct);
        var matrix = new IPEndPoint(addresses.First(a => a.AddressFamily == AddressFamily.InterNetwork), ticket.MatrixPort);

        _socket = new UdpClient(0, AddressFamily.InterNetwork);

        // The socket listens on every address, a recording needs one: the loopback for a local server
        var localPort = ((IPEndPoint)_socket.Client.LocalEndPoint!).Port;
        _local = new IPEndPoint(IPAddress.IsLoopback(matrix.Address) ? IPAddress.Loopback : IPAddress.Any, localPort);

        _session = await MatrixHandshake.RunAsync(_socket, matrix, _protocol, ct,
            (datagram, fromServer) => Recorder?.Write(DateTime.UtcNow, fromServer ? matrix : _local, fromServer ? _local : matrix, datagram));
        Log?.Invoke(this, $"socket {_session.SocketId:x8}, game server {_session.GameServer}");

        // Every sequenced channel starts at the HUGG's sequence number, the server's and the client's alike
        foreach (var channel in new[] { Channel.Matrix, Channel.ReliableGss, Channel.UnreliableGss })
        {
            _inbound[channel] = new InboundChannel(channel, _session.MatrixSequenceStart);
            _outbound[channel] = new OutboundChannel(channel, _session.MatrixSequenceStart);
        }

        var now = DateTime.UtcNow;
        SendTimeSync();
        SendMatrix(MatrixMessages.Login(CharacterGuid, ticket.Ticket), MatrixMessage.Login, now);
        await FlushAsync(ct);

        _loop = RunAsync(_stop.Token);
    }

    public async ValueTask DisposeAsync()
    {
        if (_socket != null && _session != null && State != PodState.Closed)
        {
            SendControl(ControlMessages.Close());
            try
            {
                await FlushAsync(CancellationToken.None);
            }
            catch (SocketException)
            {
            }
        }

        await _stop.CancelAsync();
        if (_loop != null)
        {
            try
            {
                await _loop;
            }
            catch (OperationCanceledException)
            {
            }
        }

        State = PodState.Closed;
        _socket?.Dispose();
        _stop.Dispose();
    }

    private async Task RunAsync(CancellationToken ct)
    {
        var receive = _socket!.ReceiveAsync(ct).AsTask();
        try
        {
            while (!ct.IsCancellationRequested && State != PodState.Closed)
            {
                var wait = Task.Delay(_timings.Tick, ct);
                if (await Task.WhenAny(receive, wait) == receive)
                {
                    var result = await receive;
                    var now = DateTime.UtcNow;
                    Recorder?.Write(now, result.RemoteEndPoint, _local!, result.Buffer);
                    HandleDatagram(result.Buffer, now);
                    receive = _socket.ReceiveAsync(ct).AsTask();
                }

                OnTick(DateTime.UtcNow);
                await FlushAsync(ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception e) when (e is SocketException or InvalidOperationException or IndexOutOfRangeException)
        {
            Failure = e.Message;
            State = PodState.Closed;
            Log?.Invoke(this, $"stopped: {e.Message}");
        }
    }

    private void HandleDatagram(byte[] datagram, DateTime now)
    {
        DatagramsReceived++;
        _bytesDown += datagram.Length;

        var reliableChannels = new HashSet<Channel>();
        foreach (var frame in Framing.Parse(datagram, out var socketId))
        {
            if (socketId != _session!.SocketId || frame.Payload.IsEmpty)
            {
                continue;
            }

            if (frame.Channel == Channel.Control)
            {
                HandleControl(frame.Payload.Span, now);
                continue;
            }

            var inbound = _inbound[frame.Channel];
            if (inbound.IsReliable)
            {
                reliableChannels.Add(frame.Channel);
            }

            foreach (var message in inbound.Accept(frame, now))
            {
                if (frame.Channel == Channel.Matrix)
                {
                    HandleMatrix(message, now);
                }
                else if (GssMessages.TryParse(message, out var gss))
                {
                    HandleGss(gss);
                }
            }
        }

        // One ack per reliable channel and datagram: the client sent 0.6 GSSAcks per reliable frame
        foreach (var channel in reliableChannels)
        {
            var inbound = _inbound[channel];
            SendControl(ControlMessages.Ack(channel, inbound.Expected, inbound.HeldSequences));
        }
    }

    private void HandleControl(ReadOnlySpan<byte> payload, DateTime now)
    {
        if (ControlMessages.TryReadAck(payload, out var channel, out var sequence))
        {
            _outbound[channel].Acked(sequence, now);
        }
        else if (payload[0] == ControlMessages.TimeSyncResponse && payload.Length >= 9)
        {
            _lastTimeSyncReceived = _uptime.Elapsed;
            var sentMicros = BitConverter.ToUInt64(payload.Slice(1, 8));
            RoundTrip = TimeSpan.FromMilliseconds(_uptime.ElapsedMilliseconds - (long)(sentMicros / 1000));
        }
        else if (ControlMessages.TryReadClose(payload, out var code))
        {
            ServerShutdownCode = code;
            State = PodState.Closed;
            Log?.Invoke(this, $"server closed the connection, code {code}");
        }
    }

    private void HandleMatrix(ReadOnlyMemory<byte> payload, DateTime now)
    {
        if (!MatrixMessages.TryDecode(payload, _protocol.Matrix, out var kind, out var body))
        {
            return;
        }

        if (kind == MatrixMessage.EnterZone)
        {
            var zone = MatrixMessages.Unpack<EnterZone>(body);
            _clock = new GameClock(zone);
            _instanceId = zone.InstanceId;
            ZoneId = zone.ZoneId;
            _enterZoneAt = now;
            TimeToZone = now - _droppedAt;
            State = PodState.LoadingZone;
            Log?.Invoke(this, $"entering zone {zone.ZoneId} ({zone.ZoneName}) after {TimeToZone.Value.TotalSeconds:F1} s");
        }
    }

    private void HandleGss(GssPayload message)
    {
        if (_world.Apply(message))
        {
            return;
        }

        // The server puts the character where it spawns with a ForcedMovement, the pod stands there from then on
        if (message.EntityId == CharacterEntity && message.Typecode == _forcedMovementTypecode && message.MessageId == _forcedMovementId)
        {
            var move = new ForcedMovement();
            move.Unpack(message.Body.Span);
            if (move.Data.Type == 1)
            {
                Position = move.Data.Params1.Position;
            }
        }
    }

    private void OnTick(DateTime now)
    {
        var uptime = _uptime.Elapsed;
        if (uptime - _lastTimeSyncSent >= _timings.TimeSync && uptime - _lastTimeSyncReceived >= _timings.TimeSync)
        {
            SendTimeSync();
        }

        if (State >= PodState.LoadingZone && now - _lastClientStatus >= _timings.ClientStatus)
        {
            SendClientStatus(now);
        }

        if (State == PodState.LoadingZone && now - _enterZoneAt >= _timings.ZoneLoad)
        {
            SendMatrix(new EnterZoneAck { Flags = 0, SpectatorTargetGuid = 0 }, MatrixMessage.EnterZoneAck, now);
            _enterZoneAckAt = now;
            State = PodState.EnteringWorld;
        }

        if (State == PodState.EnteringWorld
            && (_world.HasController(CharacterEntity, _baseControllerTypecode) || now - _enterZoneAckAt >= _timings.ReadyFallback))
        {
            SendGss(Channel.ReliableGss, GssMessages.EncodeCommand(
                new PlayerReady { ControllerReady = 1, StreamingComplete = 0, HaveSquadMemberIds = 1, SquadMemberIds = [] },
                GssCharacterView.BaseController, GssCharacterCommand.PlayerReady, CharacterGuid, _protocol.Gss), now);
            _readyAt = now;
            TimeToWorld = now - _droppedAt;
            State = PodState.InWorld;
            Log?.Invoke(this, $"in the world after {TimeToWorld.Value.TotalSeconds:F1} s");
        }

        if (State == PodState.InWorld)
        {
            if (!_streamingSent && now - _readyAt >= _timings.Streaming)
            {
                SendGss(Channel.ReliableGss, GssMessages.EncodeCommand(
                    new PlayerReady { ControllerReady = 0, StreamingComplete = 1, HaveSquadMemberIds = 0 },
                    GssCharacterView.BaseController, GssCharacterCommand.PlayerReady, CharacterGuid, _protocol.Gss), now);
                _streamingSent = true;
            }

            // Scheduled against a due time, the loop's 15.6 ms timer resolution on Windows would stretch "every 50 ms"
            // to 62 ms otherwise. After a hitch it starts over instead of sending a burst.
            if (now >= _nextMovement)
            {
                SendMovement(now);
                _nextMovement = now - _nextMovement > _timings.Movement ? now + _timings.Movement : _nextMovement + _timings.Movement;
            }
        }

        if (now - _lastKeyframeRequest >= _timings.KeyframeRequests)
        {
            SendKeyframeRequests(now);
        }

        foreach (var outbound in _outbound.Values)
        {
            _outbox.AddRange(outbound.DueResends(now));

            // Where the client gives up on the connection ("excess packet loss")
            if (outbound.Lost > 0 && State != PodState.Closed)
            {
                Failure = $"excess packet loss on {outbound.Channel}";
                State = PodState.Closed;
                Log?.Invoke(this, $"closing: {Failure}");
            }
        }
    }

    private void SendMovement(DateTime now)
    {
        var time = _clock!.Now(now);
        var shortTime = GameClock.Short(time);

        // Before the ForcedMovement the client doesn't know where it is, PIN also ignores poses until the respawn
        if (Position is { } position)
        {
            var onGround = (short)Math.Min(short.MaxValue, (now - _readyAt).TotalMilliseconds);
            var input = new MovementInput
            {
                ShortTime = shortTime,
                HorizontalInput = 0,
                VerticalInput = 0,
                HeadingPitch = default,
                HeadingYaw = default,
                InputFlags = 0,
                PoseData = new MovementPoseData
                {
                    ShortTime = shortTime,
                    MovementType = MovementDataType.PosRotState | MovementDataType.Aim,
                    WaterLevelAndDesc = 0,
                    PosRotState = new MovementPosRotState { Pos = position, Rot = Quaternion.Identity, MovementState = StandingMovementState },
                    Velocity = Vector3.Zero,
                    Aim = Vector3.UnitX,
                    JetpackEnergy = FullJetpack,
                    GroundTimePositiveAirTimeNegative = onGround,
                    TimeSinceLastJump = onGround,
                    HaveDebugData = 0,
                },
                EndShort = unchecked((ushort)(shortTime - (InterpolationDelay * 5))),
                InterpolationDelay = InterpolationDelay,
            };
            SendGss(Channel.UnreliableGss, GssMessages.EncodeCommand(input, GssCharacterView.BaseController, GssCharacterCommand.MovementInput, CharacterGuid, _protocol.Gss), now);
        }

        SendGss(Channel.UnreliableGss, GssMessages.EncodeRoot(new ScheduleUpdateRequest { Time = time }, GssMessage.ScheduleUpdateRequest, _instanceId, _protocol.Gss), now);
    }

    private void SendClientStatus(DateTime now)
    {
        var elapsed = _lastClientStatus == default ? 0 : (now - _lastClientStatus).TotalMilliseconds;
        _lastClientStatus = now;
        SendMatrix(new ClientStatus
        {
            ClientTime = (uint)_uptime.ElapsedMilliseconds,
            RoundTripTime = (ushort)Math.Min(ushort.MaxValue, RoundTrip?.TotalMilliseconds ?? 0),
            ElapsedMs = (ushort)Math.Min(ushort.MaxValue, elapsed),
            BytesDown = (uint)Math.Min(uint.MaxValue, Interlocked.Exchange(ref _bytesDown, 0)),
            BytesUp = (uint)Math.Min(uint.MaxValue, Interlocked.Exchange(ref _bytesUp, 0)),
            QualityOfServiceDown = new AeroMessages.Common.HalfFloat { Value = 15360 }, // 1.0, what the client always sent
            QualityOfServiceUp = new AeroMessages.Common.HalfFloat { Value = 15360 },
            Unk8 = [],
        }, MatrixMessage.ClientStatus, now);
    }

    private void SendKeyframeRequests(DateTime now)
    {
        _lastKeyframeRequest = now;
        var requests = _world.TakeKeyframeRequests();
        if (requests.Count == 0)
        {
            return;
        }

        SendMatrix(new KeyframeRequest
        {
            HaveRequestByEntityID = 1,
            EntityRequests = requests.Select(r => new RequestByEntity { Entity = r.Entity | r.Typecode, RefID = 0xffff, ChecksumStatus = 0, Checksum = 0 }).ToArray(),
            HaveRequestByRefID = 0,
        }, MatrixMessage.KeyframeRequest, now);
    }

    private void SendTimeSync()
    {
        _lastTimeSyncSent = _uptime.Elapsed;
        SendControl(ControlMessages.TimeSync(Microseconds()));
    }

    private void SendMatrix(IAero message, MatrixMessage kind, DateTime now) =>
        _outbox.AddRange(_outbound[Channel.Matrix].Send(MatrixMessages.Encode(message, kind, _protocol.Matrix), now));

    private void SendGss(Channel channel, byte[] payload, DateTime now) => _outbox.AddRange(_outbound[channel].Send(payload, now));

    private void SendControl(byte[] payload) => _outbox.Add(Framing.Frame(Channel.Control, 0, payload));

    private async Task FlushAsync(CancellationToken ct)
    {
        foreach (var datagram in _outbox.TakeDatagrams(_session!.SocketId))
        {
            await _socket!.SendAsync(datagram, _session.GameServer, ct);
            var now = DateTime.UtcNow;
            Recorder?.Write(now, _local!, _session.GameServer, datagram);
            DatagramsSent++;
            _bytesUp += datagram.Length;
        }
    }

    // The client sends timeGetTime() * 1000, milliseconds of uptime written as microseconds
    private ulong Microseconds() => (ulong)_uptime.ElapsedMilliseconds * 1000;
}
