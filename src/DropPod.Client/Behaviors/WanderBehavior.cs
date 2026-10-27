using System.Numerics;

namespace DropPod.Client.Behaviors;

/// <summary>
/// Runs to a random point near its spawn, rests a few seconds, then picks the next one. The pod has no collision, it
/// keeps the spawn's height, so on a slope it floats or sinks a bit. PIN takes the pose as it comes.
/// </summary>
public sealed class WanderBehavior(Random random) : IBehavior
{
    public const float Radius = 20;
    public const float RunSpeed = 6.2f; // the client's speed while running on the ground in the capture

    // A step is 0.3 m at 20 poses per second, more after a hitch
    private const float Arrived = 1;

    private Vector3? _target;
    private DateTime _restUntil;

    public WanderBehavior()
        : this(Random.Shared)
    {
    }

    public void Tick(Pod pod, DateTime now)
    {
        if (pod.Spawn is not { } spawn || pod.Position is not { } position)
        {
            return;
        }

        if (_target is { } target)
        {
            if (Vector2.Distance(new Vector2(position.X, position.Y), new Vector2(target.X, target.Y)) <= Arrived)
            {
                pod.Stand();
                _target = null;
                _restUntil = now + TimeSpan.FromSeconds(2 + (random.NextDouble() * 6));
            }
            else
            {
                pod.RunTowards(target, RunSpeed);
            }
        }
        else if (now >= _restUntil)
        {
            _target = spawn + RandomOffset();
            pod.RunTowards(_target.Value, RunSpeed);
        }
    }

    // Square root, so the points spread evenly over the disc instead of bunching up around the spawn
    private Vector3 RandomOffset()
    {
        var distance = Radius * MathF.Sqrt(random.NextSingle());
        var angle = random.NextSingle() * MathF.Tau;
        return new Vector3(MathF.Cos(angle) * distance, MathF.Sin(angle) * distance, 0);
    }
}
