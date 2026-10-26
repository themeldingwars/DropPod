using System.Buffers.Binary;

namespace DropPod.Client.Net;

/// <summary>Numbered as on the wire.</summary>
public enum Channel : byte
{
    Control = 0,
    Matrix = 1,
    ReliableGss = 2,
    UnreliableGss = 3,
}

/// <summary>
/// One frame of a datagram. <see cref="Sequence"/> is null on the Control channel, the only one without sequence
/// numbers. <see cref="Payload"/> has the resend XOR undone.
/// </summary>
public readonly record struct Frame(Channel Channel, byte ResendCount, bool IsSplit, ushort? Sequence, ReadOnlyMemory<byte> Payload);

/// <summary>
/// The datagram layout after the handshake: the socket id (uint32, big-endian) followed by frames. A frame is a 2-byte
/// big-endian header (channel 2 bits, resend count 2 bits, split 1 bit, length 11 bits, header included), a sequence
/// number on every channel but Control, and the payload.
/// </summary>
public static class Framing
{
    public const int SocketIdSize = 4;
    public const int HeaderSize = 2;
    public const int SequenceSize = 2;
    public const int MaxFrameLength = 0x7FF;

    public static bool IsSequenced(Channel channel) => channel != Channel.Control;

    /// <summary>A resent frame has the data after its sequence number XORed with this, same as PIN's Channel.</summary>
    public static byte ResendXor(int resendCount) => resendCount switch
    {
        1 => 0xFF,
        2 => 0xAA,
        3 => 0xCC,
        _ => 0x00,
    };

    public static ushort PackHeader(Channel channel, byte resendCount, bool isSplit, int length)
    {
        if (length is < HeaderSize or > MaxFrameLength)
        {
            throw new ArgumentOutOfRangeException(nameof(length), length, "A frame is 2 to 2047 bytes long");
        }

        return (ushort)(((byte)channel << 14) | ((resendCount & 0b11) << 12) | ((isSplit ? 1 : 0) << 11) | length);
    }

    public static int WriteFrame(Span<byte> destination, Channel channel, ushort sequence, ReadOnlySpan<byte> payload, bool isSplit = false)
    {
        var framing = HeaderSize + (IsSequenced(channel) ? SequenceSize : 0);
        var length = framing + payload.Length;

        BinaryPrimitives.WriteUInt16BigEndian(destination, PackHeader(channel, 0, isSplit, length));
        if (IsSequenced(channel))
        {
            BinaryPrimitives.WriteUInt16BigEndian(destination[HeaderSize..], sequence);
        }

        payload.CopyTo(destination[framing..]);
        return length;
    }

    public static byte[] Datagram(uint socketId, Channel channel, ushort sequence, ReadOnlySpan<byte> payload, bool isSplit = false)
    {
        var length = HeaderSize + (IsSequenced(channel) ? SequenceSize : 0) + payload.Length;
        var datagram = new byte[SocketIdSize + length];
        BinaryPrimitives.WriteUInt32BigEndian(datagram, socketId);
        WriteFrame(datagram.AsSpan(SocketIdSize), channel, sequence, payload, isSplit);
        return datagram;
    }

    /// <summary>Stops at a frame that runs past the end, like PIN does.</summary>
    public static List<Frame> Parse(ReadOnlyMemory<byte> datagram, out uint socketId)
    {
        var frames = new List<Frame>();
        var span = datagram.Span;
        socketId = span.Length >= SocketIdSize ? BinaryPrimitives.ReadUInt32BigEndian(span) : 0;

        var offset = SocketIdSize;
        while (offset + HeaderSize <= span.Length)
        {
            var header = BinaryPrimitives.ReadUInt16BigEndian(span[offset..]);
            var channel = (Channel)(header >> 14);
            var resendCount = (byte)((header >> 12) & 0b11);
            var isSplit = ((header >> 11) & 1) == 1;
            var length = header & MaxFrameLength;

            if (length < HeaderSize || offset + length > span.Length)
            {
                break;
            }

            ushort? sequence = null;
            var dataStart = offset + HeaderSize;
            if (IsSequenced(channel))
            {
                if (length < HeaderSize + SequenceSize)
                {
                    break;
                }

                sequence = BinaryPrimitives.ReadUInt16BigEndian(span[dataStart..]);
                dataStart += SequenceSize;
            }

            var payload = datagram[dataStart..(offset + length)];
            if (resendCount > 0)
            {
                var copy = payload.ToArray();
                var xor = ResendXor(resendCount);
                for (var i = 0; i < copy.Length; i++)
                {
                    copy[i] ^= xor;
                }

                payload = copy;
            }

            frames.Add(new Frame(channel, resendCount, isSplit, sequence, payload));
            offset += length;
        }

        return frames;
    }
}
