using System.Globalization;

namespace DropPod.Client.Invasions;

/// <summary>Pods per second or minute, "5/s" or "30/m". The ramp down takes the same pace.</summary>
public readonly record struct Ramp(double PerSecond)
{
    public static readonly Ramp Default = new(5);

    public TimeSpan Gap => TimeSpan.FromSeconds(1 / PerSecond);

    public static bool TryParse(string? text, out Ramp ramp)
    {
        ramp = default;
        var parts = text?.Split('/') ?? [];
        if (parts.Length != 2
            || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var count)
            || count <= 0)
        {
            return false;
        }

        switch (parts[1])
        {
            case "s":
                ramp = new Ramp(count);
                return true;
            case "m":
                ramp = new Ramp(count / 60);
                return true;
            default:
                return false;
        }
    }

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{PerSecond:0.##}/s");
}
