using System.Buffers.Binary;

namespace DropPod.Client.Net;

/// <summary>
/// Acks and CloseConnection carry big-endian numbers, the time sync a little-endian one, so they're written by hand
/// instead of through Aero.
/// </summary>
public static class ControlMessages
{
    public const byte CloseConnection = 0;
    public const byte MatrixAck = 2;
    public const byte GssAck = 3;
    public const byte TimeSyncRequest = 4;
    public const byte TimeSyncResponse = 5;
    public const byte MtuProbe = 6;

    /// <summary>
    /// Like the client: the sequence number expected next, the last one in order, then each frame that arrived past
    /// the gap. The server only resends what isn't in there.
    /// </summary>
    public static byte[] Ack(Channel channel, ushort nextExpected, IReadOnlyList<ushort>? pastGap = null)
    {
        pastGap ??= [];
        var payload = new byte[5 + (pastGap.Count * 2)];
        payload[0] = channel == Channel.Matrix ? MatrixAck : GssAck;
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(1), nextExpected);
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(3), unchecked((ushort)(nextExpected - 1)));
        for (var i = 0; i < pastGap.Count; i++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(5 + (i * 2)), pastGap[i]);
        }

        return payload;
    }

    public static bool TryReadAck(ReadOnlySpan<byte> payload, out Channel channel, out ushort ackedSequence)
    {
        channel = default;
        ackedSequence = 0;
        if (payload.Length < 5 || payload[0] is not (MatrixAck or GssAck))
        {
            return false;
        }

        channel = payload[0] == MatrixAck ? Channel.Matrix : Channel.ReliableGss;
        ackedSequence = BinaryPrimitives.ReadUInt16BigEndian(payload[3..]);
        return true;
    }

    public static byte[] TimeSync(ulong clientMicroseconds)
    {
        var payload = new byte[9];
        payload[0] = TimeSyncRequest;
        BinaryPrimitives.WriteUInt64LittleEndian(payload.AsSpan(1), clientMicroseconds);
        return payload;
    }

    /// <summary>Without a code or reason, what a client sends when it logs out.</summary>
    public static byte[] Close() => [CloseConnection, 0, 0, 0, 0];

    public static bool TryReadClose(ReadOnlySpan<byte> payload, out uint shutdownCode)
    {
        shutdownCode = 0;
        if (payload.IsEmpty || payload[0] != CloseConnection)
        {
            return false;
        }

        if (payload.Length >= 5)
        {
            shutdownCode = BinaryPrimitives.ReadUInt32BigEndian(payload[1..]);
        }

        return true;
    }
}
