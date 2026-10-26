using AeroMessages.Matrix;

namespace DropPod.Client.World;

/// <summary>
/// The game time in ScheduleUpdateRequest (32-bit milliseconds) and the movement messages (its low 16 bits).
/// SimulationSeedMs in EnterZone is the game time at SimulationSeedUnixMicros.
/// </summary>
public sealed class GameClock
{
    private readonly uint _seedMs;
    private readonly long _seedUnixMicros;
    private readonly double _timescale;
    private readonly long _offsetMicros;

    public GameClock(EnterZone zone)
    {
        _seedMs = zone.SimulationSeedMs;
        _seedUnixMicros = (long)zone.GameClockInfo.SimulationSeedUnixMicros;
        _timescale = zone.GameClockInfo.Timescale == 0 ? 1 : zone.GameClockInfo.Timescale;
        _offsetMicros = (long)zone.GameClockInfo.ClockOffsetMicros;
    }

    public uint Now(DateTime utcNow)
    {
        var unixMicros = (utcNow - DateTime.UnixEpoch).Ticks / 10;
        var elapsedMs = (unixMicros + _offsetMicros - _seedUnixMicros) * _timescale / 1000;
        return unchecked(_seedMs + (uint)(long)elapsedMs);
    }

    public static ushort Short(uint time) => unchecked((ushort)time);
}
