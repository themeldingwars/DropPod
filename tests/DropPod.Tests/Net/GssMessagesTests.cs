using Aero.Protocol;
using AeroMessages.GSS.Character.Command;
using AeroMessages.GSS.Generic;
using DropPod.Client.Net;

namespace DropPod.Tests.Net;

/// <summary>
/// Wire ids as the 1962 client sent them: MovementInput on typecode 2 with message id 115, ScheduleUpdateRequest to
/// the instance on typecode 251 with message id 19.
/// </summary>
[TestClass]
public sealed class GssMessagesTests
{
    private static readonly GssVersion Version = ClientProtocol.Build1962.Gss;

    [TestMethod]
    public void MovementInputGoesOutLikeTheClients()
    {
        var payload = GssMessages.EncodeCommand(new MovementInput(), GssCharacterView.BaseController, GssCharacterCommand.MovementInput, 0x7f3cb864ab3492fe, Version);
        Assert.IsTrue(GssMessages.TryParse(payload, out var message));

        Assert.AreEqual((byte)2, message.Typecode);
        Assert.AreEqual((byte)115, message.MessageId);
        Assert.AreEqual(0x7f3cb864ab349200ul, message.EntityId);
    }

    [TestMethod]
    public void ScheduleUpdateRequestGoesToTheInstance()
    {
        var payload = GssMessages.EncodeRoot(new ScheduleUpdateRequest { Time = 1681337899 }, GssMessage.ScheduleUpdateRequest, 0x4658119e701000, Version);
        Assert.IsTrue(GssMessages.TryParse(payload, out var message));

        Assert.AreEqual(GssMessages.ClientRootTypecode, message.Typecode);
        Assert.AreEqual((byte)19, message.MessageId);
        Assert.AreEqual(0x4658119e701000ul, message.EntityId);
    }

    [TestMethod]
    public void PlayerReadyGoesToTheBaseController()
    {
        var payload = GssMessages.EncodeCommand(new PlayerReady(), GssCharacterView.BaseController, GssCharacterCommand.PlayerReady, 0x7f3cb864ab3492fe, Version);
        Assert.IsTrue(GssMessages.TryParse(payload, out var message));

        GssTables.TryGetWireIds(Version, GssCharacterView.BaseController, GssCharacterCommand.PlayerReady, out var typecode, out var messageId);
        Assert.AreEqual((byte)2, message.Typecode);
        Assert.AreEqual(typecode, message.Typecode);
        Assert.AreEqual(messageId, message.MessageId);
    }
}
