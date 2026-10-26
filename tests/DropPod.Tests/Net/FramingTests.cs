using DropPod.Client.Net;

namespace DropPod.Tests.Net;

/// <summary>Datagrams the 1962 client sent right after the handshake, taken from a capture.</summary>
[TestClass]
public sealed class FramingTests
{
    [TestMethod]
    public void ParsesAControlFrame()
    {
        var frames = Framing.Parse(Convert.FromHexString("B00E63BC000B046003DC5D44000000"), out var socketId);

        Assert.AreEqual(0xb00e63bcu, socketId);
        Assert.HasCount(1, frames);
        Assert.AreEqual(Channel.Control, frames[0].Channel);
        Assert.IsNull(frames[0].Sequence);
        CollectionAssert.AreEqual(Convert.FromHexString("046003DC5D44000000"), frames[0].Payload.ToArray());
    }

    [TestMethod]
    public void ParsesAControlAndAMatrixFrame()
    {
        var datagram = Convert.FromHexString("B00E63BC000702CA06CA05401BCA04194E7C8011CF000504B80000004D050000003C003C0000");
        var frames = Framing.Parse(datagram, out _);

        Assert.HasCount(2, frames);
        Assert.AreEqual(Channel.Control, frames[0].Channel);
        Assert.AreEqual(Channel.Matrix, frames[1].Channel);
        Assert.AreEqual((ushort)0xca04, frames[1].Sequence);
        Assert.AreEqual(0x19, frames[1].Payload.Span[0]);
        Assert.AreEqual(27 - 4, frames[1].Payload.Length);
    }

    [TestMethod]
    public void WritesWhatItParses()
    {
        var payload = Convert.FromHexString("194E7C8011CF000504B80000004D050000003C003C0000");
        var datagram = Framing.Datagram(0xb00e63bc, Channel.Matrix, 0xca04, payload);

        CollectionAssert.AreEqual(Convert.FromHexString("B00E63BC401BCA04194E7C8011CF000504B80000004D050000003C003C0000"), datagram);
    }

    [TestMethod]
    public void UndoesTheResendXor()
    {
        // Matrix frame, resend count 1: header 0x5007, the data after the sequence number XORed with 0xFF
        var frames = Framing.Parse(Convert.FromHexString("B00E63BC50070001EEDDCC"), out _);

        Assert.AreEqual((byte)1, frames[0].ResendCount);
        CollectionAssert.AreEqual(new byte[] { 0x11, 0x22, 0x33 }, frames[0].Payload.ToArray());
    }

    [TestMethod]
    public void StopsAtATruncatedFrame()
    {
        Assert.HasCount(0, Framing.Parse(Convert.FromHexString("B00E63BC401BCA0419"), out _));
    }
}
