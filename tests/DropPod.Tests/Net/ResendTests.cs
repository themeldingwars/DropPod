using DropPod.Client.Net;

namespace DropPod.Tests.Net;

[TestClass]
public sealed class ResendTests
{
    [TestMethod]
    public void ResentFrameParsesBackToItsPayload()
    {
        var payload = new byte[] { 0x11, 0x22, 0x33 };
        var frame = Framing.Frame(Channel.Matrix, 51717, payload, resendCount: 3);

        // 0xCC for the third resend, the only count the live servers used
        CollectionAssert.AreEqual(Convert.FromHexString("7007CA05DDEEFF"), frame);

        var parsed = Framing.Parse(new byte[Framing.SocketIdSize].Concat(frame).ToArray(), out _);
        CollectionAssert.AreEqual(payload, parsed[0].Payload.ToArray());
    }

    [TestMethod]
    public void ReadsAnAck()
    {
        Assert.IsTrue(ControlMessages.TryReadAck(Convert.FromHexString("03CA06CA05"), out var channel, out var sequence));
        Assert.AreEqual(Channel.ReliableGss, channel);
        Assert.AreEqual((ushort)0xca05, sequence);
    }
}
