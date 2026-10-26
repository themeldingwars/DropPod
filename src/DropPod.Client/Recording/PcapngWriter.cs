using System.Buffers.Binary;
using System.Net;

namespace DropPod.Client.Recording;

/// <summary>Raw IPv4 packets (link type 101), so a pod's connection reads like a capture of the real client.</summary>
public sealed class PcapngWriter : IDisposable
{
    private const int LinkTypeRawIp = 101;

    private readonly Stream _stream;
    private readonly object _lock = new();

    public PcapngWriter(string path)
        : this(File.Create(path))
    {
    }

    public PcapngWriter(Stream stream)
    {
        _stream = stream;

        // Section header: byte order magic, version 1.0, unknown section length
        Span<byte> section = stackalloc byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(section, 0x1A2B3C4D);
        BinaryPrimitives.WriteUInt16LittleEndian(section[4..], 1);
        BinaryPrimitives.WriteInt64LittleEndian(section[8..], -1);
        WriteBlock(0x0A0D0D0A, section);

        // Interface description: link type, reserved, no snap length limit. Timestamps default to microseconds
        Span<byte> iface = stackalloc byte[8];
        BinaryPrimitives.WriteUInt16LittleEndian(iface, LinkTypeRawIp);
        WriteBlock(1, iface);
    }

    public void Write(DateTime utc, IPEndPoint source, IPEndPoint destination, ReadOnlySpan<byte> payload)
    {
        var packet = new byte[20 + 8 + payload.Length];

        // IPv4 header, don't fragment, TTL 64, UDP
        packet[0] = 0x45;
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(2), (ushort)packet.Length);
        packet[6] = 0x40;
        packet[8] = 64;
        packet[9] = 17;
        source.Address.MapToIPv4().TryWriteBytes(packet.AsSpan(12, 4), out _);
        destination.Address.MapToIPv4().TryWriteBytes(packet.AsSpan(16, 4), out _);
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(10), Checksum(packet.AsSpan(0, 20)));

        // UDP header without checksum, which is optional on IPv4
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(20), (ushort)source.Port);
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(22), (ushort)destination.Port);
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(24), (ushort)(8 + payload.Length));
        payload.CopyTo(packet.AsSpan(28));

        var micros = (ulong)((utc - DateTime.UnixEpoch).Ticks / 10);
        var padded = (packet.Length + 3) & ~3;
        var body = new byte[20 + padded];
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(4), (uint)(micros >> 32));
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(8), (uint)micros);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(12), (uint)packet.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(16), (uint)packet.Length);
        packet.CopyTo(body, 20);

        lock (_lock)
        {
            WriteBlock(6, body);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _stream.Dispose();
        }
    }

    private void WriteBlock(uint type, ReadOnlySpan<byte> body)
    {
        Span<byte> number = stackalloc byte[4];
        var length = (uint)(12 + body.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(number, type);
        _stream.Write(number);
        BinaryPrimitives.WriteUInt32LittleEndian(number, length);
        _stream.Write(number);
        _stream.Write(body);
        _stream.Write(number);
    }

    private static ushort Checksum(ReadOnlySpan<byte> header)
    {
        uint sum = 0;
        for (var i = 0; i < header.Length; i += 2)
        {
            sum += BinaryPrimitives.ReadUInt16BigEndian(header[i..]);
        }

        while (sum > 0xffff)
        {
            sum = (sum & 0xffff) + (sum >> 16);
        }

        return (ushort)~sum;
    }
}
