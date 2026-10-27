namespace DropPod.Client.Behaviors;

/// <summary>
/// Stands for 30 to 90 s, logs out and comes back 5 to 15 s later, over and over. Every round trip is a new ticket,
/// handshake, character load and scope in on the server.
/// </summary>
public sealed class ChurnBehavior(Random random) : IBehavior
{
    private DateTime _leaveAt;

    public ChurnBehavior()
        : this(Random.Shared)
    {
    }

    public void Tick(Pod pod, DateTime now)
    {
        if (_leaveAt == default)
        {
            _leaveAt = now + TimeSpan.FromSeconds(30 + (random.NextDouble() * 60));
        }
        else if (now >= _leaveAt && pod.LeaveFor == null)
        {
            pod.Leave(TimeSpan.FromSeconds(5 + (random.NextDouble() * 10)));
        }
    }
}
