using System.Numerics;
using DropPod.Client.World;

namespace DropPod.Tests.World;

[TestClass]
public sealed class HeadingTests
{
    // A running MovementInput from the capture: velocity (3.214, 3.383, 0), rotation (0, 0, 0.3708, 0.9287), HeadingYaw 0x3A14
    [TestMethod]
    public void MatchesTheClientWhileRunning()
    {
        var yaw = Heading.Yaw(new Vector2(3.214f, 3.383f));
        var rotation = Heading.Rotation(yaw);
        var forward = Heading.Forward(yaw);

        Assert.AreEqual(0.3708f, rotation.Z, 0.001f);
        Assert.AreEqual(0.9287f, rotation.W, 0.001f);
        Assert.AreEqual(0.689f, forward.X, 0.001f);
        Assert.AreEqual(0.725f, forward.Y, 0.001f);
        Assert.AreEqual(0x3A14, Heading.Half(yaw), 1);
    }
}
