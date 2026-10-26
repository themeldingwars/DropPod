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

    /// <summary>The sequence number expected next, then the one acked.</summary>
    public static byte[] Ack(Channel channel, ushort ackedSequence)
    {
        var payload = new byte[5];
        payload[0] = channel == Channel.Matrix ? MatrixAck : GssAck;
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(1), unchecked((ushort)(ackedSequence + 1)));
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(3), ackedSequence);
        return payload;
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
