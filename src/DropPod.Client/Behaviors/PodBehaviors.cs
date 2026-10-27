namespace DropPod.Client.Behaviors;

public static class PodBehaviors
{
    public static readonly string[] Names = ["idle", "wander", "chat"];

    public static bool IsKnown(string name) => Names.Contains(name);

    /// <summary>A new instance per pod, behaviors keep state.</summary>
    public static IBehavior Create(string name) => name switch
    {
        "idle" => new IdleBehavior(),
        "wander" => new WanderBehavior(),
        "chat" => new ChatBehavior(),
        _ => throw new ArgumentException($"Unknown behavior {name}, expected one of {string.Join(", ", Names)}", nameof(name)),
    };
}

/// <summary>Stands where it spawned, the pod still sends its pose and update requests like a client standing still.</summary>
public sealed class IdleBehavior : IBehavior
{
    public void Tick(Pod pod, DateTime now)
    {
    }
}
