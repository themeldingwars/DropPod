using DropPod.Client.Net;

namespace DropPod.Tests.Net;

[TestClass]
public sealed class ControlMessagesTests
{
    [TestMethod]
    public void AckMatchesTheCapture()
    {
        // The 1962 client acking the server's Matrix frame CA04, taken from a capture
        CollectionAssert.AreEqual(Convert.FromHexString("02CA05CA04"), ControlMessages.Ack(Channel.Matrix, 0xca05));
    }

    [TestMethod]
    public void AckWrapsAround()
    {
        CollectionAssert.AreEqual(Convert.FromHexString("030000FFFF"), ControlMessages.Ack(Channel.ReliableGss, 0x0000));
    }

    [TestMethod]
    public void AckListsTheFramesPastTheGap()
    {
        CollectionAssert.AreEqual(Convert.FromHexString("030010000F00120013"), ControlMessages.Ack(Channel.ReliableGss, 0x0010, [0x0012, 0x0013]));
    }

    [TestMethod]
    public void ReadsTheShutdownCode()
    {
        Assert.IsTrue(ControlMessages.TryReadClose(Convert.FromHexString("0000000007"), out var code));
        Assert.AreEqual(7u, code);
    }
}
