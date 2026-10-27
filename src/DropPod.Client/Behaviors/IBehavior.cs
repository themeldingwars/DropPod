namespace DropPod.Client.Behaviors;

/// <summary>What a pod does once it's in the world. Ticked on the pod's loop, so it needs no lock.</summary>
public interface IBehavior
{
    void Tick(Pod pod, DateTime now);
}
