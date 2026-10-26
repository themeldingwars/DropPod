using DropPod.Client.Net;

namespace DropPod.Tests.Net;

[TestClass]
public sealed class InboundChannelTests
{
    private static readonly DateTime Now = new(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);

    private static Frame Reliable(ushort sequence, byte value, bool split = false) => new(Channel.ReliableGss, 0, split, sequence, new byte[] { value });

    [TestMethod]
    public void HandsMessagesOnInOrder()
    {
        var channel = new InboundChannel(Channel.ReliableGss, 51715);

        Assert.HasCount(1, channel.Accept(Reliable(51715, 1), Now));
        Assert.HasCount(1, channel.Accept(Reliable(51716, 2), Now));
        Assert.AreEqual(0, channel.Held);
    }

    [TestMethod]
    public void HoldsFramesPastAGap()
    {
        var channel = new InboundChannel(Channel.ReliableGss, 51715);

        Assert.HasCount(0, channel.Accept(Reliable(51716, 2), Now));
        Assert.HasCount(0, channel.Accept(Reliable(51717, 3), Now));
        Assert.AreEqual(2, channel.Held);
        Assert.AreEqual(Now, channel.GapSince);

        var messages = channel.Accept(Reliable(51715, 1), Now.AddSeconds(1));
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, messages.Select(m => m.Span[0]).ToArray());
        Assert.IsNull(channel.GapSince);
    }

    [TestMethod]
    public void DropsDuplicates()
    {
        var channel = new InboundChannel(Channel.ReliableGss, 51715);
        channel.Accept(Reliable(51715, 1), Now);

        Assert.HasCount(0, channel.Accept(Reliable(51715, 1), Now));
        Assert.AreEqual(1, channel.Duplicates);
    }

    [TestMethod]
    public void JoinsSplitMessages()
    {
        var channel = new InboundChannel(Channel.ReliableGss, 51715);

        Assert.HasCount(0, channel.Accept(Reliable(51715, 1, split: true), Now));
        Assert.HasCount(0, channel.Accept(Reliable(51716, 2, split: true), Now));
        var messages = channel.Accept(Reliable(51717, 3), Now);

        Assert.HasCount(1, messages);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, messages[0].ToArray());
    }

    [TestMethod]
    public void WrapsAroundAtTheEndOfTheSequenceNumbers()
    {
        var channel = new InboundChannel(Channel.Matrix, 0xffff);

        Assert.HasCount(1, channel.Accept(Reliable(0xffff, 1) with { Channel = Channel.Matrix }, Now));
        Assert.HasCount(1, channel.Accept(Reliable(0, 2) with { Channel = Channel.Matrix }, Now));
    }

    [TestMethod]
    public void UnreliableFramesPassEvenWhenLate()
    {
        var channel = new InboundChannel(Channel.UnreliableGss, 51715);

        Assert.HasCount(1, channel.Accept(new Frame(Channel.UnreliableGss, 0, false, 51717, new byte[] { 3 }), Now));
        Assert.HasCount(1, channel.Accept(new Frame(Channel.UnreliableGss, 0, false, 51716, new byte[] { 2 }), Now));
        Assert.AreEqual(1, channel.Late);
    }
}
