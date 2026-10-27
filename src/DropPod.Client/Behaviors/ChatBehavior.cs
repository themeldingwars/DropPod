using DropPod.Client.World;

namespace DropPod.Client.Behaviors;

/// <summary>
/// Stands at its spawn and talks on Zone chat every 20 s or so. PIN sends every Zone message to every player in the
/// zone, so the cost grows with the square of the pods.
/// </summary>
public sealed class ChatBehavior(Random random) : IBehavior
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(20);

    private DateTime _next;
    private int _sent;

    public ChatBehavior()
        : this(Random.Shared)
    {
    }

    public void Tick(Pod pod, DateTime now)
    {
        // The first message lands anywhere in the first interval, so pods dropped together don't all talk at once
        if (_next == default)
        {
            _next = now + (Interval * random.NextDouble());
            return;
        }

        if (now < _next)
        {
            return;
        }

        _sent++;
        pod.Say(ChatChannel.Zone, $"{pod.Name} checking in, message {_sent}");
        _next = now + (Interval * (0.5 + random.NextDouble()));
    }
}
