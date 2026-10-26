namespace DropPod.Client.Net;

/// <summary>Numbers the frames, splits long messages and resends reliable frames the server doesn't ack.</summary>
/// <remarks>
/// Resends work like the client's (FUN_00fe55e0, FUN_00fe59e0, FUN_00fe4fb0): one timeout per channel, it starts at
/// 1000 ms, every resend makes it 1.5 times longer and every ack pulls it towards 2.5 times the frame's round trip,
/// always within 250 to 2500 ms. The client's fast retransmit isn't done, it needs the selective acks PIN doesn't send.
/// </remarks>
public sealed class OutboundChannel
{
    /// <summary>
    /// The client caps a datagram at its TargetMTU of 1168 bytes (the Login preferences). When the client splits wasn't
    /// checked.
    /// </summary>
    public const int MaxPayload = 1168 - Framing.SocketIdSize - Framing.HeaderSize - Framing.SequenceSize;

    public const int InitialResendTimeoutMs = 1000;

    public const int MinResendTimeoutMs = 250;

    public const int MaxResendTimeoutMs = 2500;

    /// <summary>The client closes the connection when a frame still isn't acked after this many resends.</summary>
    public const int MaxResends = 15;

    private readonly Dictionary<ushort, Pending> _unacked = [];
    private ushort _next;

    public OutboundChannel(Channel channel, ushort firstSequence)
    {
        if (!Framing.IsSequenced(channel))
        {
            throw new ArgumentException("The Control channel has no sequence numbers", nameof(channel));
        }

        Channel = channel;
        _next = firstSequence;
    }

    public Channel Channel { get; }

    public bool IsReliable => Channel is Channel.Matrix or Channel.ReliableGss;

    public int Unacked => _unacked.Count;

    public int ResendTimeoutMs { get; private set; } = InitialResendTimeoutMs;

    public long Resent { get; private set; }

    /// <summary>Frames still not acked after <see cref="MaxResends"/> resends, where the client gives up on the connection.</summary>
    public long Lost { get; private set; }

    public List<byte[]> Send(ReadOnlySpan<byte> payload, DateTime now)
    {
        var frames = new List<byte[]>();
        var offset = 0;
        do
        {
            var length = Math.Min(MaxPayload, payload.Length - offset);
            var part = payload.Slice(offset, length);
            offset += length;

            var sequence = _next;
            _next = unchecked((ushort)(_next + 1));
            var isSplit = offset < payload.Length;
            frames.Add(Framing.Frame(Channel, sequence, part, isSplit));

            if (IsReliable)
            {
                _unacked[sequence] = new Pending(part.ToArray(), isSplit, now, 0);
            }
        }
        while (offset < payload.Length);

        return frames;
    }

    /// <summary>Acks every frame up to <paramref name="sequence"/>, the client reads an ack the same way.</summary>
    public void Acked(ushort sequence, DateTime now)
    {
        foreach (var (pendingSequence, pending) in _unacked.ToList())
        {
            if (unchecked((short)(sequence - pendingSequence)) < 0)
            {
                continue;
            }

            _unacked.Remove(pendingSequence);
            var roundTrip = (int)Math.Max(0, (now - pending.SentAt).TotalMilliseconds);
            ResendTimeoutMs = Clamp((((roundTrip + 1) / 2) + (roundTrip * 2) + ResendTimeoutMs) / 2);
        }
    }

    public List<byte[]> DueResends(DateTime now)
    {
        var frames = new List<byte[]>();
        foreach (var (sequence, pending) in _unacked.ToList())
        {
            if ((now - pending.SentAt).TotalMilliseconds < ResendTimeoutMs)
            {
                continue;
            }

            if (pending.Resends >= MaxResends)
            {
                _unacked.Remove(sequence);
                Lost++;
                continue;
            }

            ResendTimeoutMs = Clamp(ResendTimeoutMs + (ResendTimeoutMs / 2));

            // The client counts the first send too and writes count % 3 + 1, so its resends go 3, 1, 2, 3, ...
            var resends = pending.Resends + 1;
            frames.Add(Framing.Frame(Channel, sequence, pending.Payload, pending.IsSplit, (byte)(((resends + 1) % 3) + 1)));
            _unacked[sequence] = pending with { SentAt = now, Resends = resends };
            Resent++;
        }

        return frames;
    }

    private static int Clamp(int timeoutMs) => Math.Clamp(timeoutMs, MinResendTimeoutMs, MaxResendTimeoutMs);

    private sealed record Pending(byte[] Payload, bool IsSplit, DateTime SentAt, int Resends);
}
