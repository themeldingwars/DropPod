using System.Buffers.Binary;
using System.Net;
using DropPod.Client.Recording;

namespace DropPod.Tests.Recording;

[TestClass]
public sealed class PcapngWriterTests
{
    [TestMethod]
    public void WritesARawIpPacketPerDatagram()
    {
        var stream = new MemoryStream();
        var poke = Convert.FromHexString("00000000504F4B450004B968");
        var time = new DateTime(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);
        using (var writer = new PcapngWriter(stream))
        {
            writer.Write(time, IPEndPoint.Parse("127.0.0.1:51278"), IPEndPoint.Parse("127.0.0.1:25000"), poke);
        }

        var data = stream.ToArray();
        Assert.AreEqual(0x0A0D0D0Au, BinaryPrimitives.ReadUInt32LittleEndian(data));

        // Section header (28 bytes), then the interface with link type 101
        Assert.AreEqual(1u, BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(28)));
        Assert.AreEqual((ushort)101, BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(36)));

        // Interface block (20 bytes), then the packet block
        var block = data.AsSpan(48);
        Assert.AreEqual(6u, BinaryPrimitives.ReadUInt32LittleEndian(block));
        var micros = ((ulong)BinaryPrimitives.ReadUInt32LittleEndian(block[12..]) << 32) | BinaryPrimitives.ReadUInt32LittleEndian(block[16..]);
        Assert.AreEqual(time, DateTime.UnixEpoch.AddTicks((long)micros * 10));

        var ip = block[28..];
        Assert.AreEqual(0x45, ip[0]);
        Assert.AreEqual(17, ip[9]);
        Assert.AreEqual((ushort)51278, BinaryPrimitives.ReadUInt16BigEndian(ip[20..]));
        Assert.AreEqual((ushort)25000, BinaryPrimitives.ReadUInt16BigEndian(ip[22..]));
        CollectionAssert.AreEqual(poke, ip.Slice(28, poke.Length).ToArray());
    }
}
