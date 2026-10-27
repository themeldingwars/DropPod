using DropPod.Client.Invasions;

namespace DropPod.Tests.Invasions;

[TestClass]
public sealed class InvasionTests
{
    [TestMethod]
    public void ReadsTheGroupsAndSettings()
    {
        var invasion = Invasion.Parse("""
            {
              // comments and trailing commas are fine
              "operator": "https://192.168.1.230:5001",
              "ramp": "2/s",
              "hold": "10m",
              "groups": [
                { "pods": 150, "behavior": "wander", "zone": 448 },
                { "pods": 20, "behavior": "churn" },
              ]
            }
            """);

        Assert.AreEqual(new Uri("https://192.168.1.230:5001"), invasion.Operator);
        Assert.AreEqual(new Ramp(2), invasion.Ramp);
        Assert.AreEqual(TimeSpan.FromMinutes(10), invasion.Hold);
        Assert.AreEqual(170, invasion.Pods);
        Assert.AreEqual(new InvasionGroup(150, "wander", 448), invasion.Groups[0]);
        Assert.AreEqual(new InvasionGroup(20, "churn", null), invasion.Groups[1]);
    }

    [TestMethod]
    public void LeavesOutWhatTheFileDoesNotSet()
    {
        var invasion = Invasion.Parse("""{ "groups": [ { "pods": 1 } ] }""");

        Assert.IsNull(invasion.Operator);
        Assert.IsNull(invasion.Ramp);
        Assert.IsNull(invasion.Hold);
        Assert.AreEqual(new InvasionGroup(1, "idle", null), invasion.Groups[0]);
    }

    [TestMethod]
    [DataRow("""{ "groups": [] }""")]
    [DataRow("""{ "groups": [ { "pods": 0 } ] }""")]
    [DataRow("""{ "groups": [ { "pods": 1, "behavior": "dance" } ] }""")]
    [DataRow("""{ "ramp": "fast", "groups": [ { "pods": 1 } ] }""")]
    [DataRow("""{ "hold": "forever", "groups": [ { "pods": 1 } ] }""")]
    [DataRow("""{ "groups": """)]
    public void RejectsBrokenFiles(string json) => Assert.ThrowsExactly<InvalidDataException>(() => Invasion.Parse(json));

    [TestMethod]
    [DataRow("90", 90.0)]
    [DataRow("90s", 90.0)]
    [DataRow("10m", 600.0)]
    [DataRow("1.5h", 5400.0)]
    public void ReadsDurations(string text, double seconds)
    {
        Assert.IsTrue(Invasion.TryParseDuration(text, out var duration));
        Assert.AreEqual(TimeSpan.FromSeconds(seconds), duration);
    }

    [TestMethod]
    public void ShipsAValidExample()
    {
        var invasion = Invasion.Load(Path.Combine(RepoRoot(), "invasions", "new-eden-200.json"));
        Assert.AreEqual(200, invasion.Pods);
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "DropPod.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("DropPod.slnx not found above the test binaries");
    }
}
