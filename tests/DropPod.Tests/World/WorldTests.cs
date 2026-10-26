using AeroMessages.Matrix;
using DropPod.Client.Net;
using DropPod.Client.World;

namespace DropPod.Tests.World;

[TestClass]
public sealed class WorldTests
{
    [TestMethod]
    public void GameClockStartsAtTheSeed()
    {
        // The values PIN puts into EnterZone
        var zone = new EnterZone
        {
            SimulationSeedMs = 0x63e2db5e,
            GameClockInfo = new GameClockInfoData { SimulationSeedUnixMicros = 1478774752697322, Timescale = 1.0 },
        };
        var clock = new GameClock(zone);
        var seed = DateTime.UnixEpoch.AddTicks(1478774752697322 * 10);

        Assert.AreEqual(0x63e2db5eu, clock.Now(seed));
        Assert.AreEqual(0x63e2db5eu + 1500, clock.Now(seed.AddMilliseconds(1500)));
    }

    [TestMethod]
    public void KeyframesScopeInAndOut()
    {
        var table = new EntityTable();
        table.Apply(new GssPayload(13, 0x1000, GssMessages.ViewKeyframe, default));
        table.Apply(new GssPayload(14, 0x1000, GssMessages.ViewKeyframe, default));

        Assert.AreEqual(1, table.Entities);
        Assert.AreEqual(2, table.Views);

        table.Apply(new GssPayload(13, 0x1000, GssMessages.ScopeOut, default));
        Assert.AreEqual(1, table.Views);
        Assert.AreEqual(1, table.ScopedOut);
    }

    [TestMethod]
    public void UpdateWithoutKeyframeAsksForOneOnce()
    {
        var table = new EntityTable();
        table.Apply(new GssPayload(13, 0x2000, GssMessages.Update, default));
        table.Apply(new GssPayload(13, 0x2000, GssMessages.Checksum, default));

        Assert.HasCount(1, table.TakeKeyframeRequests());
        Assert.HasCount(0, table.TakeKeyframeRequests());

        table.Apply(new GssPayload(13, 0x2000, GssMessages.ViewKeyframe, default));
        table.Apply(new GssPayload(13, 0x2000, GssMessages.Update, default));
        Assert.HasCount(0, table.TakeKeyframeRequests());
    }
}
