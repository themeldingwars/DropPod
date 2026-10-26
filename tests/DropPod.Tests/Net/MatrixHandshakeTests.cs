using DropPod.Client.Net;

namespace DropPod.Tests.Net;

/// <summary>The bytes are from a capture of the 1962 client (socket b00e63bc, game port 35639).</summary>
[TestClass]
public sealed class MatrixHandshakeTests
{
    [TestMethod]
    public void ProtocolOf1962()
    {
        Assert.AreEqual((ushort)47464, ClientProtocol.Build1962.RawMatrix);
        Assert.AreEqual((ushort)19551, ClientProtocol.Build1962.RawGss);
    }

    [TestMethod]
    public void PokeMatchesTheCapture()
    {
        CollectionAssert.AreEqual(Convert.FromHexString("00000000504F4B450004B968"), MatrixHandshake.Poke(47464));
    }

    [TestMethod]
    public void KissMatchesTheCapture()
    {
        CollectionAssert.AreEqual(Convert.FromHexString("000000004B495353B00E63BC4C5F"), MatrixHandshake.Kiss(0xb00e63bc, 19551));
    }

    [TestMethod]
    public void ReadsHeheOfBothLengths()
    {
        Assert.IsTrue(MatrixHandshake.TryReadHehe(Convert.FromHexString("0000000048454845B00E63BC000000000000"), out var live));
        Assert.AreEqual(0xb00e63bcu, live);

        Assert.IsTrue(MatrixHandshake.TryReadHehe(Convert.FromHexString("0000000048454845B00E63BC"), out var pin));
        Assert.AreEqual(0xb00e63bcu, pin);
    }

    [TestMethod]
    public void ReadsHugg()
    {
        Assert.IsTrue(MatrixHandshake.TryReadHugg(Convert.FromHexString("0000000048554747CA038B37000000000000"), out var sequence, out var port));
        Assert.AreEqual((ushort)51715, sequence);
        Assert.AreEqual((ushort)35639, port);
    }

    [TestMethod]
    public void GameTrafficIsNoHandshake()
    {
        Assert.IsNull(MatrixHandshake.ReadType(Convert.FromHexString("B00E63BC000B046003DC5D44000000")));
    }
}
