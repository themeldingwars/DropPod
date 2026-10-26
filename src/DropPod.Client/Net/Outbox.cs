using System.Buffers.Binary;

namespace DropPod.Client.Net;

/// <summary>Packs frames into as few datagrams as the MTU allows, the client sent about 1.7 frames per datagram.</summary>
public sealed class Outbox
{
    public const int MaxDatagram = 1168;

    private readonly List<byte[]> _frames = [];
    private readonly object _lock = new();

    public void Add(byte[] frame)
    {
        lock (_lock)
        {
            _frames.Add(frame);
        }
    }

    public void AddRange(IEnumerable<byte[]> frames)
    {
        lock (_lock)
        {
            _frames.AddRange(frames);
        }
    }

    public List<byte[]> TakeDatagrams(uint socketId)
    {
        List<byte[]> frames;
        lock (_lock)
        {
            if (_frames.Count == 0)
            {
                return [];
            }

            frames = [.. _frames];
            _frames.Clear();
        }

        var datagrams = new List<byte[]>();
        var current = new List<byte[]>();
        var size = Framing.SocketIdSize;
        foreach (var frame in frames)
        {
            if (current.Count > 0 && size + frame.Length > MaxDatagram)
            {
                datagrams.Add(Pack(socketId, current, size));
                current.Clear();
                size = Framing.SocketIdSize;
            }

            current.Add(frame);
            size += frame.Length;
        }

        datagrams.Add(Pack(socketId, current, size));
        return datagrams;
    }

    private static byte[] Pack(uint socketId, List<byte[]> frames, int size)
    {
        var datagram = new byte[size];
        BinaryPrimitives.WriteUInt32BigEndian(datagram, socketId);
        var offset = Framing.SocketIdSize;
        foreach (var frame in frames)
        {
            frame.CopyTo(datagram, offset);
            offset += frame.Length;
        }

        return datagram;
    }
}
