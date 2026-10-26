using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace DropPod.Client.Net;

/// <summary>
/// POKE, HEHE (socket id), KISS, HUGG (game server port and first sequence number), or ABRT when the server refuses.
/// Every handshake datagram starts with socket id 0 and a 4-character type, the fields after it are big-endian.
/// </summary>
public static class MatrixHandshake
{
    /// <summary>Checked by the server before the protocol number.</summary>
    public const ushort HandshakeVersion = 4;

    /// <summary>The 1962 client sent its POKE again after these delays when the server didn't answer.</summary>
    public static readonly TimeSpan[] PokeRetries =
    [
        TimeSpan.FromMilliseconds(400), TimeSpan.FromMilliseconds(800), TimeSpan.FromMilliseconds(1200),
        TimeSpan.FromMilliseconds(1600), TimeSpan.FromMilliseconds(2000), TimeSpan.FromMilliseconds(2400),
        TimeSpan.FromMilliseconds(2800),
    ];

    public static byte[] Poke(ushort matrixProtocol)
    {
        var datagram = new byte[12];
        Encoding.ASCII.GetBytes("POKE", datagram.AsSpan(4));
        BinaryPrimitives.WriteUInt16BigEndian(datagram.AsSpan(8), HandshakeVersion);
        BinaryPrimitives.WriteUInt16BigEndian(datagram.AsSpan(10), matrixProtocol);
        return datagram;
    }

    public static byte[] Kiss(uint socketId, ushort gssProtocol)
    {
        var datagram = new byte[14];
        Encoding.ASCII.GetBytes("KISS", datagram.AsSpan(4));
        BinaryPrimitives.WriteUInt32BigEndian(datagram.AsSpan(8), socketId);
        BinaryPrimitives.WriteUInt16BigEndian(datagram.AsSpan(12), gssProtocol);
        return datagram;
    }

    /// <summary>Null for anything but a handshake datagram, game traffic has a socket id up front.</summary>
    public static string? ReadType(ReadOnlySpan<byte> datagram)
    {
        if (datagram.Length < 8 || BinaryPrimitives.ReadUInt32BigEndian(datagram) != 0)
        {
            return null;
        }

        return Encoding.ASCII.GetString(datagram.Slice(4, 4));
    }

    /// <summary>PIN sends 12 bytes, the live servers sent 18 (six zero bytes after the id).</summary>
    public static bool TryReadHehe(ReadOnlySpan<byte> datagram, out uint socketId)
    {
        socketId = 0;
        if (ReadType(datagram) != "HEHE" || datagram.Length < 12)
        {
            return false;
        }

        socketId = BinaryPrimitives.ReadUInt32BigEndian(datagram.Slice(8));
        return socketId != 0;
    }

    public static bool TryReadHugg(ReadOnlySpan<byte> datagram, out ushort sequenceStart, out ushort gameServerPort)
    {
        sequenceStart = gameServerPort = 0;
        if (ReadType(datagram) != "HUGG" || datagram.Length < 12)
        {
            return false;
        }

        sequenceStart = BinaryPrimitives.ReadUInt16BigEndian(datagram.Slice(8));
        gameServerPort = BinaryPrimitives.ReadUInt16BigEndian(datagram.Slice(10));
        return true;
    }

    /// <summary>ABRT carries a reason: 1 for an unknown handshake version, 2 for an unknown protocol (client code).</summary>
    public static bool TryReadAbrt(ReadOnlySpan<byte> datagram, out byte code)
    {
        code = 0;
        if (ReadType(datagram) != "ABRT" || datagram.Length < 9)
        {
            return false;
        }

        code = datagram[8];
        return true;
    }

    /// <summary>The game server is on the Matrix server's host, at the port HUGG names.</summary>
    public static async Task<MatrixSession> RunAsync(UdpClient socket, IPEndPoint matrix, ClientProtocol protocol, CancellationToken ct)
    {
        var hehe = (await ExchangeAsync(socket, matrix, Poke(protocol.RawMatrix), "HEHE", ct)).Buffer;
        if (!TryReadHehe(hehe, out var id))
        {
            throw new MatrixHandshakeException("HEHE without a socket id");
        }

        var hugg = (await ExchangeAsync(socket, matrix, Kiss(id, protocol.RawGss), "HUGG", ct)).Buffer;
        TryReadHugg(hugg, out var sequenceStart, out var port);

        return new MatrixSession(id, new IPEndPoint(matrix.Address, port), sequenceStart);
    }

    private static async Task<UdpReceiveResult> ExchangeAsync(UdpClient socket, IPEndPoint matrix, byte[] request, string expected, CancellationToken ct)
    {
        foreach (var wait in PokeRetries)
        {
            await socket.SendAsync(request, matrix, ct);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(wait);
            try
            {
                while (true)
                {
                    var result = await socket.ReceiveAsync(timeout.Token);
                    var type = ReadType(result.Buffer);
                    if (type == expected)
                    {
                        return result;
                    }

                    if (TryReadAbrt(result.Buffer, out var code))
                    {
                        throw new MatrixHandshakeException($"The Matrix server aborted the handshake with code {code}");
                    }
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Nothing in time, send again
            }
        }

        throw new MatrixHandshakeException($"No {expected} from {matrix}");
    }
}

public sealed record MatrixSession(uint SocketId, IPEndPoint GameServer, ushort MatrixSequenceStart);

public sealed class MatrixHandshakeException(string message) : Exception(message);
