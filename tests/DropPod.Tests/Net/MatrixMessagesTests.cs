using Aero.Protocol;
using DropPod.Client.Net;

namespace DropPod.Tests.Net;

[TestClass]
public sealed class MatrixMessagesTests
{
    private static readonly MatrixVersion Version = ClientProtocol.Build1962.Matrix;

    /// <summary>
    /// The start of a Login the 1962 client sent, up to the preferences. Red5Sig2 and the ticket are left out, the pods
    /// send neither the same way.
    /// </summary>
    [TestMethod]
    public void LoginStartsLikeTheCapture()
    {
        var payload = MatrixMessages.Encode(MatrixMessages.Login(0x7f3cb864ab3492fe, [0x52, 0x35, 0x20]), MatrixMessage.Login, Version);

        var captured = Convert.FromHexString("1100AA07000000FE9234AB64B83C7F007D000000000000900400");
        CollectionAssert.AreEqual(captured, payload[..captured.Length]);
    }

    [TestMethod]
    public void LoginRoundTrips()
    {
        var ticket = new byte[35];
        ticket[0] = (byte)'R';
        var payload = MatrixMessages.Encode(MatrixMessages.Login(0x7f3cb864ab3492fe, ticket), MatrixMessage.Login, Version);

        Assert.IsTrue(MatrixMessages.TryDecode(payload, Version, out var kind, out var body));
        Assert.AreEqual(MatrixMessage.Login, kind);

        var login = MatrixMessages.Unpack<AeroMessages.Matrix.Login>(body);
        Assert.AreEqual(0x7f3cb864ab3492feul, login.CharacterGuid);
        CollectionAssert.AreEqual(ticket, login.Ticket);
    }
}
