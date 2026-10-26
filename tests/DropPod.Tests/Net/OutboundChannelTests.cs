using DropPod.Client.Net;

namespace DropPod.Tests.Net;

[TestClass]
public sealed class OutboundChannelTests
{
    private static readonly DateTime Now = new(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);

    [TestMethod]
    public void NumbersFramesFromTheSequenceStart()
    {
        var channel = new OutboundChannel(Channel.Matrix, 51715);
        var first = channel.Send(new byte[] { 0x11 }, Now);
        var second = channel.Send(new byte[] { 0x19 }, Now);

        Assert.AreEqual((ushort)51715, Framing.Parse(Datagram(first), out _)[0].Sequence);
        Assert.AreEqual((ushort)51716, Framing.Parse(Datagram(second), out _)[0].Sequence);
        Assert.AreEqual(2, channel.Unacked);
    }

    [TestMethod]
    public void SplitsLongMessages()
    {
        var channel = new OutboundChannel(Channel.ReliableGss, 1);
        var frames = channel.Send(new byte[OutboundChannel.MaxPayload + 10], Now);

        Assert.HasCount(2, frames);
        var parsed = frames.Select(f => Framing.Parse(Datagram([f]), out _)[0]).ToList();
        Assert.IsTrue(parsed[0].IsSplit);
        Assert.IsFalse(parsed[1].IsSplit);
        Assert.AreEqual(10, parsed[1].Payload.Length);

        var inbound = new InboundChannel(Channel.ReliableGss, 1);
        inbound.Accept(parsed[0], Now);
        Assert.AreEqual(OutboundChannel.MaxPayload + 10, inbound.Accept(parsed[1], Now)[0].Length);
    }

    [TestMethod]
    public void FirstResendAfterASecondWithResendCountThree()
    {
        var channel = new OutboundChannel(Channel.Matrix, 51715);
        channel.Send(new byte[] { 0x11 }, Now);

        Assert.HasCount(0, channel.DueResends(Now.AddMilliseconds(999)));
        var resends = channel.DueResends(Now.AddMilliseconds(1000));

        Assert.HasCount(1, resends);
        var frame = Framing.Parse(Datagram(resends), out _)[0];
        Assert.AreEqual((byte)3, frame.ResendCount);
        Assert.AreEqual(0x11, frame.Payload.Span[0]);
        Assert.AreEqual(1500, channel.ResendTimeoutMs);
    }

    [TestMethod]
    public void ResendsBackOffAndCycleTheResendCount()
    {
        var channel = new OutboundChannel(Channel.Matrix, 51715);
        channel.Send(new byte[] { 0x11 }, Now);

        var time = Now;
        var counts = new List<byte>();
        foreach (var wait in new[] { 1000, 1500, 2250, 2500 })
        {
            time = time.AddMilliseconds(wait);
            counts.Add(Framing.Parse(Datagram(channel.DueResends(time)), out _)[0].ResendCount);
        }

        CollectionAssert.AreEqual(new byte[] { 3, 1, 2, 3 }, counts);
        Assert.AreEqual(OutboundChannel.MaxResendTimeoutMs, channel.ResendTimeoutMs);
    }

    [TestMethod]
    public void AckPullsTheTimeoutTowardsTheRoundTrip()
    {
        var channel = new OutboundChannel(Channel.Matrix, 51715);
        channel.Send(new byte[] { 0x11 }, Now);
        channel.Acked(51715, Now.AddMilliseconds(100));

        // (50 + 200 + 1000) / 2, the client's formula for a 100 ms round trip
        Assert.AreEqual(625, channel.ResendTimeoutMs);
    }

    [TestMethod]
    public void AckCoversEveryFrameBeforeIt()
    {
        var channel = new OutboundChannel(Channel.Matrix, 0xfffe);
        for (var i = 0; i < 3; i++)
        {
            channel.Send(new byte[] { 0x11 }, Now);
        }

        channel.Acked(0, Now.AddMilliseconds(50));

        Assert.AreEqual(0, channel.Unacked);
        Assert.HasCount(0, channel.DueResends(Now.AddSeconds(5)));
    }

    [TestMethod]
    public void GivesUpAfterFifteenResends()
    {
        var channel = new OutboundChannel(Channel.ReliableGss, 51715);
        channel.Send(new byte[] { 0x11 }, Now);

        var time = Now;
        for (var i = 0; i < OutboundChannel.MaxResends; i++)
        {
            time = time.AddMilliseconds(OutboundChannel.MaxResendTimeoutMs);
            Assert.HasCount(1, channel.DueResends(time));
        }

        Assert.HasCount(0, channel.DueResends(time.AddMilliseconds(OutboundChannel.MaxResendTimeoutMs)));
        Assert.AreEqual(1, channel.Lost);
        Assert.AreEqual(0, channel.Unacked);
    }

    [TestMethod]
    public void UnreliableFramesAreNeverResent()
    {
        var channel = new OutboundChannel(Channel.UnreliableGss, 51715);
        channel.Send(new byte[] { 0x02 }, Now);

        Assert.HasCount(0, channel.DueResends(Now.AddSeconds(5)));
    }

    [TestMethod]
    public void OutboxPacksFramesUpToTheMtu()
    {
        var outbox = new Outbox();
        outbox.AddRange(Enumerable.Range(0, 3).Select(_ => new byte[500]));

        var datagrams = outbox.TakeDatagrams(0xb00e63bc);

        Assert.HasCount(2, datagrams);
        Assert.AreEqual(4 + 1000, datagrams[0].Length);
        Assert.HasCount(0, outbox.TakeDatagrams(0xb00e63bc));
    }

    private static byte[] Datagram(List<byte[]> frames) => new byte[Framing.SocketIdSize].Concat(frames.SelectMany(f => f)).ToArray();
}
