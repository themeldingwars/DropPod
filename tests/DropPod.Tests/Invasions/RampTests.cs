using DropPod.Client.Invasions;

namespace DropPod.Tests.Invasions;

[TestClass]
public sealed class RampTests
{
    [TestMethod]
    [DataRow("5/s", 5.0)]
    [DataRow("0.5/s", 0.5)]
    [DataRow("30/m", 0.5)]
    public void ReadsPodsPerSecondOrMinute(string text, double perSecond)
    {
        Assert.IsTrue(Ramp.TryParse(text, out var ramp));
        Assert.AreEqual(perSecond, ramp.PerSecond, 0.0001);
    }

    [TestMethod]
    [DataRow("5")]
    [DataRow("0/s")]
    [DataRow("-1/s")]
    [DataRow("5/h")]
    [DataRow("")]
    public void RejectsWhatIsNoRamp(string text) => Assert.IsFalse(Ramp.TryParse(text, out _));

    [TestMethod]
    public void GapIsTheTimeBetweenTwoPods() => Assert.AreEqual(TimeSpan.FromMilliseconds(200), new Ramp(5).Gap);
}
