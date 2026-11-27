using System.Buffers.Binary;
using Aero.Gen;
using AeroMessages.Control;

namespace DropPod.Client.Net;

/// <summary>
/// The message id byte, then the Aero-packed message. Control ids are the same in every protocol version.
/// </summary>
public static class ControlMessages
{
    public const byte CloseConnection = 0;
    public const byte MatrixAck = 2;
    public const byte GssAck = 3;
    public const byte TimeSyncRequest = 4;
    public const byte TimeSyncResponse = 5;
    public const byte MtuProbe = 6;

    private const int MaxCloseText = 0xf3;

    /// <summary>
    /// Like the client: the sequence number expected next, the last one in order, then each frame that arrived past
    /// the gap. The server only resends what isn't in there.
    /// </summary>
    public static byte[] Ack(Channel channel, ushort nextExpected, IReadOnlyList<ushort>? pastGap = null)
    {
        pastGap ??= [];
        var additionalAcks = new byte[pastGap.Count * 2];
        for (var i = 0; i < pastGap.Count; i++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(additionalAcks.AsSpan(i * 2), pastGap[i]);
        }

        var ackForNum = unchecked((ushort)(nextExpected - 1));
        return channel == Channel.Matrix
                   ? Encode(MatrixAck, new AeroMessages.Control.MatrixAck { NextSeqNum = nextExpected, AckForNum = ackForNum, AdditionalAcks = additionalAcks })
                   : Encode(GssAck, new GSSAck { NextSeqNum = nextExpected, AckForNum = ackForNum, AdditionalAcks = additionalAcks });
    }

    public static bool TryReadAck(ReadOnlySpan<byte> payload, out Channel channel, out ushort ackedSequence)
    {
        channel = default;
        ackedSequence = 0;
        if (payload.Length < 5 || payload[0] is not (MatrixAck or GssAck))
        {
            return false;
        }

        if (payload[0] == MatrixAck)
        {
            channel = Channel.Matrix;
            ackedSequence = Decode<AeroMessages.Control.MatrixAck>(payload).AckForNum;
        }
        else
        {
            channel = Channel.ReliableGss;
            ackedSequence = Decode<GSSAck>(payload).AckForNum;
        }

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
    public static byte[] Close() => Encode(CloseConnection, new AeroMessages.Control.CloseConnection());

    /// <summary>
    /// The client's answer to the server's CloseConnection (GameSocket_SendClose): the server's code and reason text
    /// echoed back. A close too short for a code gets 0xFFFFFFFF, the text stops at a NUL or after 243 bytes.
    /// </summary>
    public static byte[] CloseReply(ReadOnlySpan<byte> serverClose)
    {
        var code = 0u;
        var text = ReadOnlySpan<byte>.Empty;
        if (serverClose.Length >= 5)
        {
            code = BinaryPrimitives.ReadUInt32BigEndian(serverClose[1..]);
            text = serverClose[5..];
        }
        else if (serverClose.Length > 1)
        {
            code = 0xffffffff;
            text = serverClose[1..];
        }

        text = text[..Math.Min(text.Length, MaxCloseText)];
        var end = text.IndexOf((byte)0);
        if (end >= 0)
        {
            text = text[..end];
        }

        return Encode(CloseConnection, new AeroMessages.Control.CloseConnection { ShutdownCode = code, Reason = text.ToArray() });
    }

    public static bool TryReadClose(ReadOnlySpan<byte> payload, out uint shutdownCode)
    {
        shutdownCode = 0;
        if (payload.IsEmpty || payload[0] != CloseConnection)
        {
            return false;
        }

        if (payload.Length >= 5)
        {
            shutdownCode = Decode<AeroMessages.Control.CloseConnection>(payload).ShutdownCode;
        }

        return true;
    }

    private static byte[] Encode(byte messageId, IAero message)
    {
        var payload = new byte[1 + message.GetPackedSize()];
        payload[0] = messageId;
        message.Pack(payload.AsSpan(1));
        return payload;
    }

    private static T Decode<T>(ReadOnlySpan<byte> payload)
        where T : IAero, new()
    {
        var message = new T();
        message.Unpack(payload[1..]);
        return message;
    }
}
