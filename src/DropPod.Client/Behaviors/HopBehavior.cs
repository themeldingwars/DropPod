namespace DropPod.Client.Behaviors;

/// <summary>
/// Stands for 60 to 180 s, then travels to another open world zone with the zone admin command, over and over. Every
/// hop saves the zone through RIN, closes the connection and logs in again into the other zone's shard.
/// </summary>
public sealed class HopBehavior(Random random, IReadOnlyList<uint> zones) : IBehavior
{
    // New Eden, Devil's Tusk and Sertao, the open world zones with the most 1962 replays
    public static readonly uint[] DefaultZones = [448, 162, 1030];

    private DateTime _hopAt;

    public HopBehavior()
        : this(Random.Shared, DefaultZones)
    {
    }

    public void Tick(Pod pod, DateTime now)
    {
        if (_hopAt == default)
        {
            _hopAt = now + TimeSpan.FromSeconds(60 + (random.NextDouble() * 120));
        }
        else if (now >= _hopAt && pod.HopTo == null)
        {
            var others = zones.Where(z => z != pod.ZoneId).ToArray();
            if (others.Length > 0)
            {
                pod.Hop(others[random.Next(others.Length)]);
            }
        }
    }
}
