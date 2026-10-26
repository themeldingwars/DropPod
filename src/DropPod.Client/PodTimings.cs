namespace DropPod.Client;

/// <summary>The defaults reproduce the 1962 client standing still.</summary>
public sealed record PodTimings
{
    /// <summary>EnterZone to EnterZoneAck, the client loading the zone.</summary>
    public TimeSpan ZoneLoad { get; init; } = TimeSpan.FromMilliseconds(4900);

    /// <summary>
    /// The first PlayerReady goes out when the BaseController keyframe arrives, at the latest after this. The client sent
    /// it 9.9 s after the EnterZoneAck.
    /// </summary>
    public TimeSpan ReadyFallback { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>First to second PlayerReady (streaming complete).</summary>
    public TimeSpan Streaming { get; init; } = TimeSpan.FromMilliseconds(18400);

    /// <summary>MovementInput and ScheduleUpdateRequest go out in pairs, the client sent about 20 of each per second.</summary>
    public TimeSpan Movement { get; init; } = TimeSpan.FromMilliseconds(50);

    public TimeSpan ClientStatus { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>How often pending keyframe requests go out together.</summary>
    public TimeSpan KeyframeRequests { get; init; } = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// The client sends a time sync once this has passed since both the last request and the last response
    /// (FUN_00fe8e00).
    /// </summary>
    public TimeSpan TimeSync { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>How often the pod's loop wakes up when nothing arrives.</summary>
    public TimeSpan Tick { get; init; } = TimeSpan.FromMilliseconds(10);
}
